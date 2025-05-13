// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Settings;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public class EmittedStreamsTracker : IEmittedStreamsTracker {
	private static readonly ILogger Log = Serilog.Log.ForContext<EmittedStreamsTracker>();
	private readonly IODispatcher _ioDispatcher;
	private readonly ProjectionConfig _projectionConfig;
	private readonly ProjectionNamesBuilder _projectionNamesBuilder;

	private readonly BoundedCache<string, string> _streamIdCache = new BoundedCache<string, string>(int.MaxValue,
		ESConsts.CommittedEventsMemCacheLimit, x => 16 + 4 + IntPtr.Size + 2 * x.Length);

	private const int MaxRetryCount = 3;
	private readonly object _locker = new object();

	public EmittedStreamsTracker(IODispatcher ioDispatcher, ProjectionConfig projectionConfig,
		ProjectionNamesBuilder projectionNamesBuilder) {
		_ioDispatcher = ioDispatcher;
		_projectionConfig = projectionConfig;
		_projectionNamesBuilder = projectionNamesBuilder;
	}

	public void Initialize() {
		ReadEmittedStreamStreamIdsIntoCache(0); //start from the beginning
	}

	private void ReadEmittedStreamStreamIdsIntoCache(long position) {
		_ioDispatcher.ReadForward(_projectionNamesBuilder.GetEmittedStreamsName(), position, 1, false,
			SystemAccounts.System, x => {
				if (x.Events is not []) {
					for (int i = 0; i < x.Events.Count; i++) {
						var streamId = Helper.UTF8NoBom.GetString(x.Events[i].Event.Data.Span);
						lock (_locker) {
							_streamIdCache.PutRecord(streamId, streamId, false);
						}
					}
				}

				if (!x.IsEndOfStream) {
					ReadEmittedStreamStreamIdsIntoCache(x.NextEventNumber);
				}
			}, () => {
				Log.Error(
					"Timed out reading emitted stream ids into cache from {streamName} at position {position}.",
					_projectionNamesBuilder.GetEmittedStreamsName(), position);
			}, Guid.NewGuid());
	}

	public void TrackEmittedStream(EmittedEvent[] emittedEvents) {
		if (!_projectionConfig.TrackEmittedStreams)
			return;
		foreach (var emittedEvent in emittedEvents) {
			string streamId;
			if (!_streamIdCache.TryGetRecord(emittedEvent.StreamId, out streamId)) {
				var trackEvent = new Event(Guid.NewGuid(), ProjectionEventTypes.StreamTracked, false,
					Helper.UTF8NoBom.GetBytes(emittedEvent.StreamId), null, null);
				lock (_locker) {
					_streamIdCache.PutRecord(emittedEvent.StreamId, emittedEvent.StreamId, false);
				}

				WriteEvent(trackEvent, MaxRetryCount);
			}
		}
	}

	private void WriteEvent(Event evnt, int retryCount) {
		_ioDispatcher.WriteEvent(_projectionNamesBuilder.GetEmittedStreamsName(), ExpectedVersion.Any, evnt,
			SystemAccounts.System,
			x => OnWriteComplete(x, evnt, Helper.UTF8NoBom.GetString(evnt.Data), retryCount));
	}

	private void OnWriteComplete(ClientMessage.WriteEventsCompleted completed, Event evnt, string streamId,
		int retryCount) {
		if (completed.Result != OperationResult.Success) {
			if (retryCount > 0) {
				WriteEvent(evnt, retryCount - 1);
			} else {
				Log.Error(
					"PROJECTIONS: Failed to write a tracked stream id of {stream} to the {emittedStream} stream. Retry limit of {maxRetryCount} reached. Reason: {e}",
					streamId, _projectionNamesBuilder.GetEmittedStreamsName(), MaxRetryCount, completed.Result);
			}
		}
	}
}
