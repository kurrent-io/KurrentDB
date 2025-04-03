// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;

namespace EventStore.Projections.Core.Services.Processing.MultiStream;

public partial class MultiStreamMultiOutputCheckpointManager : DefaultCheckpointManager, IEmittedStreamContainer {
	private readonly PositionTagger _positionTagger;
	private CheckpointTag _lastOrderCheckpointTag; //TODO: use position tracker to ensure order?
	private EmittedStream _orderStream;
	private bool _orderStreamReadingCompleted;
	private int _loadingItemsCount;
	private readonly Stack<Item> _loadQueue = new Stack<Item>();
	private CheckpointTag _loadingPrerecordedEventsFrom;
	private static readonly char[] _linkToSeparator = new[] {'@'};

	public MultiStreamMultiOutputCheckpointManager(
		IPublisher publisher, Guid projectionCorrelationId, ProjectionVersion projectionVersion, ClaimsPrincipal runAs,
		IODispatcher ioDispatcher, ProjectionConfig projectionConfig, string name, PositionTagger positionTagger,
		ProjectionNamesBuilder namingBuilder, bool usePersistentCheckpoints, bool producesRunningResults,
		bool definesFold,
		CoreProjectionCheckpointWriter coreProjectionCheckpointWriter, int maxProjectionStateSize)
		: base(
			publisher, projectionCorrelationId, projectionVersion, runAs, ioDispatcher, projectionConfig, name,
			positionTagger, namingBuilder, usePersistentCheckpoints, producesRunningResults, definesFold,
			coreProjectionCheckpointWriter, maxProjectionStateSize) {
		_positionTagger = positionTagger;
	}

	public override void Initialize() {
		base.Initialize();
		_lastOrderCheckpointTag = null;
		if (_orderStream != null) _orderStream.Dispose();
		_orderStream = null;
	}

	public override void Start(CheckpointTag checkpointTag, PartitionState rootPartitionState) {
		base.Start(checkpointTag, rootPartitionState);
		_orderStream = CreateOrderStream(checkpointTag);
		_orderStream.Start();
	}

	public override void RecordEventOrder(
		ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action committed) {
		EnsureStarted();
		if (_stopping)
			throw new InvalidOperationException("Stopping");
		var orderStreamName = _namingBuilder.GetOrderStreamName();
		//TODO: -order stream requires correctly configured event expiration.
		// the best is to truncate using $startFrom, but $maxAge should be also acceptable
		_orderStream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					orderStreamName, Guid.NewGuid(), "$>",
					false, resolvedEvent.PositionSequenceNumber + "@" + resolvedEvent.PositionStreamId, null,
					orderCheckpointTag, _lastOrderCheckpointTag, v => committed())
			});
		_lastOrderCheckpointTag = orderCheckpointTag;
	}

	private EmittedStream CreateOrderStream(CheckpointTag from) {
		//TODO: this stream requires $startFrom to be updated from time to time to reduce space taken
		return new EmittedStream(
			/* MUST NEVER SEND READY MESSAGE */
			_namingBuilder.GetOrderStreamName(),
			new EmittedStream.WriterConfiguration(
				new EmittedStreamsWriter(_ioDispatcher), new EmittedStream.WriterConfiguration.StreamMetadata(),
				SystemAccounts.System, 100, _logger),
			_projectionVersion, _positionTagger, @from, _publisher, _ioDispatcher, this, noCheckpoints: true);
	}

	public override void GetStatistics(ProjectionStatistics info) {
		base.GetStatistics(info);
		if (_orderStream != null) {
			info.WritePendingEventsAfterCheckpoint += _orderStream.GetWritePendingEvents();
			info.ReadsInProgress += _orderStream.GetReadsInProgress();
			info.WritesInProgress += _orderStream.GetWritesInProgress();
		}
	}


	public override void BeginLoadPrerecordedEvents(CheckpointTag checkpointTag) {
		BeginLoadPrerecordedEventsChunk(checkpointTag, -1);
	}

	private void BeginLoadPrerecordedEventsChunk(CheckpointTag checkpointTag, long fromEventNumber) {
		_loadingPrerecordedEventsFrom = checkpointTag;
		_ioDispatcher.ReadBackward(
			_namingBuilder.GetOrderStreamName(), fromEventNumber, 100, false, SystemAccounts.System,
			completed => {
				switch (completed.Result) {
					case ReadStreamResult.NoStream:
						_lastOrderCheckpointTag = _positionTagger.MakeZeroCheckpointTag();
						PrerecordedEventsLoaded(checkpointTag);
						break;
					case ReadStreamResult.Success:
						var epochEnded = false;
						foreach (var @event in completed.Events) {
							var parsed = @event.Event.Metadata.ParseCheckpointTagVersionExtraJson(
								_projectionVersion);
							//TODO: throw exception if different projectionID?
							if (_projectionVersion.ProjectionId != parsed.Version.ProjectionId
							    || _projectionVersion.Epoch > parsed.Version.Version) {
								epochEnded = true;
								break;
							}

							var tag = parsed.AdjustBy(_positionTagger, _projectionVersion);
							//NOTE: even if this tag <= checkpointTag we set last tag
							// this is to know the exact last tag to request when writing
							if (_lastOrderCheckpointTag == null)
								_lastOrderCheckpointTag = tag;

							if (tag <= checkpointTag) {
								SetOrderStreamReadCompleted();
								return;
							}

							EnqueuePrerecordedEvent(@event.Event, tag);
						}

						if (epochEnded || completed.IsEndOfStream)
							SetOrderStreamReadCompleted();
						else
							BeginLoadPrerecordedEventsChunk(checkpointTag, completed.NextEventNumber);
						break;
					default:
						throw new Exception("Cannot read order stream");
				}
			}, () => {
				_logger.Warning("Read backward of stream {stream} timed out. Retrying",
					_namingBuilder.GetOrderStreamName());
				BeginLoadPrerecordedEventsChunk(checkpointTag, fromEventNumber);
			}, Guid.NewGuid());
	}

	private void EnqueuePrerecordedEvent(EventRecord @event, CheckpointTag tag) {
		if (@event == null) throw new ArgumentNullException("event");
		if (tag == null) throw new ArgumentNullException("tag");
		if (@event.EventType != "$>")
			throw new ArgumentException("linkto ($>) event expected", "event");

		_loadingItemsCount++;

		var item = new Item(tag);
		_loadQueue.Push(item);
		//NOTE: we do manual link-to resolution as we write links to the position events
		//      which may in turn be a link.  This is necessary to provide a correct
		//       ResolvedEvent when replaying from the -order stream
		var linkTo = Helper.UTF8NoBom.GetString(@event.Data.Span);
		string[] parts = linkTo.Split(_linkToSeparator, 2);
		long eventNumber = long.Parse(parts[0]);
		string streamId = parts[1];

		ReadPrerecordedEventStream(streamId, eventNumber, completed => {
			switch (completed.Result) {
				case ReadStreamResult.Success:
				case ReadStreamResult.NoStream:
				case ReadStreamResult.StreamDeleted:
					if (completed.Events.Count is 1)
						item.SetLoadedEvent(completed.Events[0]);
					_loadingItemsCount--;
					CheckAllEventsLoaded();
					break;
				default:
					throw new Exception(string.Format("Cannot read {0}. Error: {1}", linkTo, completed.Error));
			}
		});
	}

	private void ReadPrerecordedEventStream(string streamId, long eventNumber,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted> action) {
		_ioDispatcher.ReadBackward(
			streamId, eventNumber, 1, true, SystemAccounts.System, action, () => {
				_logger.Warning("Read backward of stream {stream} timed out. Retrying", streamId);
				ReadPrerecordedEventStream(streamId, eventNumber, action);
			}, Guid.NewGuid());
	}

	private void CheckAllEventsLoaded() {
		CheckpointTag lastTag = null;
		if (_orderStreamReadingCompleted && _loadingItemsCount == 0) {
			var number = 0;
			while (_loadQueue.Count > 0) {
				var item = _loadQueue.Pop();
				var @event = item._result;
				lastTag = item.Tag;
				if (@event.HasValue) {
					SendPrerecordedEvent(@event.Value, lastTag, number);
					number++;
				}
			}

			_loadingItemsCount = -1; // completed - do not dispatch one more time
			PrerecordedEventsLoaded(lastTag ?? _loadingPrerecordedEventsFrom);
		}
	}

	private void SetOrderStreamReadCompleted() {
		_orderStreamReadingCompleted = true;
		CheckAllEventsLoaded();
	}

	public void Handle(CoreProjectionProcessingMessage.EmittedStreamAwaiting message) {
		if (_stopped)
			return;
		throw new NotImplementedException();
	}

	public void Handle(CoreProjectionProcessingMessage.EmittedStreamWriteCompleted message) {
		if (_stopped)
			return;
	}
}
