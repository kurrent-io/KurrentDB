// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using Serilog;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionStreamReader : IPersistentSubscriptionStreamReader {
	private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscriptionStreamReader>();

	public const int MaxPullBatchSize = 500;
	private const int MaxRetryTimeSecs = 60;
	private const int MaxExponentialBackoffPower = 10; /*to prevent integer overflow*/

	private readonly IODispatcher _ioDispatcher;
	private readonly int _maxPullBatchSize;
	private readonly Random _random = new Random();

	public PersistentSubscriptionStreamReader(IODispatcher ioDispatcher, int maxPullBatchSize) {
		_ioDispatcher = ioDispatcher;
		_maxPullBatchSize = maxPullBatchSize;
	}

	public void BeginReadEvents(IPersistentSubscriptionEventSource eventSource,
		IPersistentSubscriptionStreamPosition startPosition, int countToLoad, int batchSize,
		int maxWindowSize, bool resolveLinkTos, bool skipFirstEvent,
		Action<IReadOnlyList<ResolvedEvent>, IPersistentSubscriptionStreamPosition, bool> onEventsFound,
		Action<IPersistentSubscriptionStreamPosition, long> onEventsSkipped,
		Action<string> onError) {
		BeginReadEventsInternal(eventSource, startPosition, countToLoad, batchSize, maxWindowSize, resolveLinkTos,
			skipFirstEvent, onEventsFound, onEventsSkipped, onError, 0);
	}

	private int GetBackOffDelay(int retryCount) {
		//exponential backoff + jitter
		return 1 + _random.Next(0, 1 + Math.Min(MaxRetryTimeSecs, (1 << Math.Min(retryCount, MaxExponentialBackoffPower))));
	}

	private void BeginReadEventsInternal(IPersistentSubscriptionEventSource eventSource,
		IPersistentSubscriptionStreamPosition startPosition, int countToLoad, int batchSize, int maxWindowSize, bool resolveLinkTos, bool skipFirstEvent,
		Action<IReadOnlyList<ResolvedEvent>, IPersistentSubscriptionStreamPosition, bool> onEventsFound,
		Action<IPersistentSubscriptionStreamPosition, long> onEventsSkipped,
		Action<string> onError, int retryCount) {
		var actualBatchSize = GetBatchSize(batchSize);

		if (eventSource.FromStream) {
			_ioDispatcher.ReadForward(
				eventSource.EventStreamId, startPosition.StreamEventNumber, Math.Min(countToLoad, actualBatchSize),
				resolveLinkTos, SystemAccounts.System, new ResponseHandler(onEventsFound, onEventsSkipped, onError, skipFirstEvent).FetchCompleted,
				async () => await HandleTimeout(eventSource.EventStreamId),
				Guid.NewGuid());
		} else if (eventSource.FromAll) {
			if (eventSource.EventFilter is null) {
				_ioDispatcher.ReadAllForward(
					startPosition.TFPosition.Commit,
					startPosition.TFPosition.Prepare,
					Math.Min(countToLoad, actualBatchSize),
					resolveLinkTos,
					true,
					null,
					SystemAccounts.System,
					null,
					new ResponseHandler(onEventsFound, onEventsSkipped, onError, skipFirstEvent).FetchAllCompleted,
					async () => await HandleTimeout(SystemStreams.AllStream),
					Guid.NewGuid());
			} else {
				var maxSearchWindow = Math.Max(actualBatchSize, maxWindowSize);
				_ioDispatcher.ReadAllForwardFiltered(
					startPosition.TFPosition.Commit,
					startPosition.TFPosition.Prepare,
					Math.Min(countToLoad, actualBatchSize),
					resolveLinkTos,
					true,
					maxSearchWindow,
					null,
					eventSource.EventFilter,
					SystemAccounts.System,
					null,
					new ResponseHandler(onEventsFound, onEventsSkipped, onError, skipFirstEvent).FetchAllFilteredCompleted,
					async () => await HandleTimeout($"{SystemStreams.AllStream} with filter {eventSource.EventFilter}"),
					Guid.NewGuid());
			}
		} else {
			throw new InvalidOperationException();
		}

		async Task HandleTimeout(string streamName) {
			var backOff = GetBackOffDelay(retryCount);
			Log.Warning(
				"Timed out reading from stream: {stream}. Retrying in {retryInterval} seconds.",
				streamName, backOff);
			await Task.Delay(TimeSpan.FromSeconds(backOff));
			BeginReadEventsInternal(eventSource, startPosition, countToLoad, batchSize, maxWindowSize, resolveLinkTos,
				skipFirstEvent, onEventsFound, onEventsSkipped, onError, retryCount + 1);
		}
	}

	private int GetBatchSize(int batchSize) {
		return Math.Min(Math.Min(batchSize == 0 ? 20 : batchSize, MaxPullBatchSize), _maxPullBatchSize);
	}

	private class ResponseHandler {
		private readonly Action<IReadOnlyList<ResolvedEvent>, IPersistentSubscriptionStreamPosition, bool> _onFetchCompleted;
		private readonly Action<IPersistentSubscriptionStreamPosition, long> _onEventsSkipped;
		private readonly Action<string> _onError;
		private readonly bool _skipFirstEvent;

		public ResponseHandler(Action<IReadOnlyList<ResolvedEvent>, IPersistentSubscriptionStreamPosition, bool> onFetchCompleted,
			Action<IPersistentSubscriptionStreamPosition, long> onEventsSkipped, Action<string> onError, bool skipFirstEvent) {
			_onFetchCompleted = onFetchCompleted;
			_onEventsSkipped = onEventsSkipped;
			_skipFirstEvent = skipFirstEvent;
			_onError = onError;
		}

		public void FetchCompleted(ClientMessage.ReadStreamEventsForwardCompleted msg) {
			switch (msg.Result) {
				case ReadStreamResult.Success:
					_onFetchCompleted(_skipFirstEvent ? msg.Events.Skip(1).ToArray() : msg.Events,
						new PersistentSubscriptionSingleStreamPosition(msg.NextEventNumber), msg.IsEndOfStream);
					break;
				case ReadStreamResult.NoStream:
					_onFetchCompleted(
						Array.Empty<ResolvedEvent>(),
						new PersistentSubscriptionSingleStreamPosition(0),
						msg.IsEndOfStream);
					break;
				case ReadStreamResult.AccessDenied:
					_onError($"Read access denied for stream: {msg.EventStreamId}");
					break;
				default:
					_onError(msg.Error ?? $"Error reading stream: {msg.EventStreamId} at event number: {msg.FromEventNumber}");
					break;
			}
		}

		public void FetchAllCompleted(ClientMessage.ReadAllEventsForwardCompleted msg) {
			switch (msg.Result) {
				case ReadAllResult.Success:
					_onFetchCompleted(_skipFirstEvent ? msg.Events.Skip(1).ToArray() : msg.Events,
						new PersistentSubscriptionAllStreamPosition(msg.NextPos.CommitPosition, msg.NextPos.PreparePosition), msg.IsEndOfStream);
					break;
				case ReadAllResult.AccessDenied:
					_onError($"Read access denied for stream: {SystemStreams.AllStream}");
					break;
				default:
					_onError(msg.Error ?? $"Error reading stream: {SystemStreams.AllStream} at position: {msg.CurrentPos}");
					break;
			}
		}
		public void FetchAllFilteredCompleted(ClientMessage.FilteredReadAllEventsForwardCompleted msg) {
			switch (msg.Result) {
				case FilteredReadAllResult.Success:
					if (msg.Events is [] && msg.ConsideredEventsCount > 0) {
						// Checkpoint on the position we read from rather than the next position
						// to prevent skipping the next event when loading from the checkpoint
						_onEventsSkipped(
							new PersistentSubscriptionAllStreamPosition(msg.CurrentPos.CommitPosition, msg.CurrentPos.PreparePosition),
							msg.ConsideredEventsCount);
					}
					_onFetchCompleted(_skipFirstEvent ? msg.Events.Skip(1).ToArray() : msg.Events,
						new PersistentSubscriptionAllStreamPosition(msg.NextPos.CommitPosition, msg.NextPos.PreparePosition), msg.IsEndOfStream);

					break;
				case FilteredReadAllResult.AccessDenied:
					_onError($"Read access denied for stream: {SystemStreams.AllStream}");
					break;
				default:
					_onError(msg.Error ?? $"Error reading stream: {SystemStreams.AllStream} at position: {msg.CurrentPos}");
					break;
			}
		}
	}
}
