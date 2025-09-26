// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Settings;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;
using UnwrapEnvelopeMessage = KurrentDB.Projections.Core.Messaging.UnwrapEnvelopeMessage;

namespace KurrentDB.Projections.Core.Services.Processing.MultiStream;

public class MultiStreamEventReader
	: EventReader,
		IHandle<ClientMessage.ReadStreamEventsForwardCompleted>,
		IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
	private readonly HashSet<string> _streams;
	private CheckpointTag _fromPositions;
	private readonly bool _resolveLinkTos;
	private readonly ITimeProvider _timeProvider;
	private readonly HashSet<string> _eventsRequested = [];
	private readonly Dictionary<string, long?> _preparePositions = new();

	// event, link, progress
	// null element in a queue means stream deleted
	private readonly Dictionary<string, Queue<(KurrentDB.Core.Data.ResolvedEvent, float)?>> _buffers = new();

	private const int MaxReadCount = 111;
	private long? _safePositionToJoin;
	private readonly ConcurrentDictionary<string, bool> _eofs;
	private long _lastPosition;

	private readonly ConcurrentDictionary<string, Guid> _pendingRequests = new();

	public MultiStreamEventReader(IPublisher publisher,
		Guid eventReaderCorrelationId,
		ClaimsPrincipal readAs,
		int phase,
		string[] streams,
		Dictionary<string, long> fromPositions,
		bool resolveLinkTos,
		ITimeProvider timeProvider,
		bool stopOnEof = false)
		: base(publisher, eventReaderCorrelationId, readAs, stopOnEof) {
		ArgumentNullException.ThrowIfNull(streams);
		ArgumentNullException.ThrowIfNull(timeProvider);
		if (streams.Length == 0)
			throw new ArgumentException("streams");
		_streams = new(streams);
		_eofs = new(_streams.ToDictionary(v => v, _ => false));
		var positions = CheckpointTag.FromStreamPositions(phase, fromPositions);
		ValidateTag(positions);
		_fromPositions = positions;
		_resolveLinkTos = resolveLinkTos;
		_timeProvider = timeProvider;
		foreach (var stream in streams) {
			_pendingRequests[stream] = Guid.Empty;
			_preparePositions.Add(stream, null);
		}
	}

	private void ValidateTag(CheckpointTag fromPositions) {
		if (_streams.Count != fromPositions.Streams.Count)
			throw new ArgumentException("Number of streams does not match", nameof(fromPositions));

		foreach (var stream in _streams) {
			if (!fromPositions.Streams.ContainsKey(stream))
				throw new ArgumentException($"The '{stream}' stream position has not been set", nameof(fromPositions));
		}
	}

	protected override void RequestEvents() {
		if (PauseRequested || Paused)
			return;
		if (_eofs.Any(v => v.Value))
			_publisher.Publish(
				TimerMessage.Schedule.Create(
					TimeSpan.FromMilliseconds(250), _publisher,
					new UnwrapEnvelopeMessage(ProcessBuffers2, nameof(ProcessBuffers2))));
		foreach (var stream in _streams)
			RequestEvents(stream, delay: _eofs[stream]);
	}

	private void ProcessBuffers2() {
		ProcessBuffers();
		CheckIdle();
	}

	protected override bool AreEventsRequested() {
		return _eventsRequested.Count != 0;
	}

	public void Handle(ClientMessage.ReadStreamEventsForwardCompleted message) {
		if (_disposed)
			return;
		if (!_streams.Contains(message.EventStreamId))
			throw new InvalidOperationException($"Invalid stream name: {message.EventStreamId}");
		if (!_eventsRequested.Contains(message.EventStreamId))
			throw new InvalidOperationException("Read events has not been requested");
		if (Paused)
			throw new InvalidOperationException("Paused");
		if (_pendingRequests.Values.All(x => x != message.CorrelationId))
			return;

		_lastPosition = message.TfLastCommitPosition;
		switch (message.Result) {
			case ReadStreamResult.StreamDeleted:
			case ReadStreamResult.NoStream:
				_eofs[message.EventStreamId] = true;
				UpdateSafePositionToJoin(message.EventStreamId, MessageToLastCommitPosition(message));
				if (message.Result == ReadStreamResult.StreamDeleted
				    || (message.Result == ReadStreamResult.NoStream && message.LastEventNumber >= 0))
					EnqueueItem(null, message.EventStreamId);
				ProcessBuffers();
				_eventsRequested.Remove(message.EventStreamId);
				PauseOrContinueProcessing();
				CheckIdle();
				CheckEof();
				break;
			case ReadStreamResult.Success:
				if (message.Events is [] && message.IsEndOfStream) {
					// the end
					_eofs[message.EventStreamId] = true;
					UpdateSafePositionToJoin(message.EventStreamId, MessageToLastCommitPosition(message));
					CheckIdle();
					CheckEof();
				} else {
					_eofs[message.EventStreamId] = false;
					if (message.Events is []) {
						_fromPositions.Streams[message.EventStreamId] = message.NextEventNumber;
					}

					for (int index = 0; index < message.Events.Count; index++) {
						var @event = message.Events[index].Event;
						var link = message.Events[index].Link;
						EventRecord positionEvent = link ?? @event;
						UpdateSafePositionToJoin(
							positionEvent.EventStreamId, EventPairToPosition(message.Events[index]));
						var itemToEnqueue = (
							message.Events[index],
							100.0f * (link ?? @event).EventNumber / message.LastEventNumber);
						EnqueueItem(itemToEnqueue, positionEvent.EventStreamId);
					}
				}

				ProcessBuffers();
				_eventsRequested.Remove(message.EventStreamId);
				PauseOrContinueProcessing();
				break;
			case ReadStreamResult.AccessDenied:
				SendNotAuthorized();
				return;
			default:
				throw new NotSupportedException($"ReadEvents result code was not recognized. Code: {message.Result}");
		}
	}

	public void Handle(ProjectionManagementMessage.Internal.ReadTimeout message) {
		if (_disposed)
			return;
		if (Paused)
			return;
		if (_pendingRequests.Values.All(x => x != message.CorrelationId))
			return;

		_eventsRequested.Remove(message.StreamId);
		PauseOrContinueProcessing();
	}

	private void EnqueueItem((KurrentDB.Core.Data.ResolvedEvent, float)? itemToEnqueue, string streamId) {
		if (!_buffers.TryGetValue(streamId, out var queue)) {
			queue = new();
			_buffers.Add(streamId, queue);
		}

		//TODO: progress calculation below is incorrect.  sum(current)/sum(last_event) where sum by all streams
		queue.Enqueue(itemToEnqueue);
	}

	private void CheckEof() {
		if (_eofs.All(v => v.Value))
			SendEof();
	}

	private void CheckIdle() {
		if (_eofs.All(v => v.Value))
			_publisher.Publish(
				new ReaderSubscriptionMessage.EventReaderIdle(EventReaderCorrelationId, _timeProvider.UtcNow));
	}

	private void ProcessBuffers() {
		if (_disposed)
			return;
		if (_safePositionToJoin == null)
			return;
		while (true) {
			var anyNonEmpty = false;

			var anyEvent = false;
			var minStreamId = "";
			var minPosition = GetMaxPosition();

			var anyDeletedStream = false;
			var deletedStreamId = "";

			foreach (var (currentStreamId, queue) in _buffers) {
				if (queue.Count == 0)
					continue;
				anyNonEmpty = true;
				var head = queue.Peek();

				if (head != null) {
					var itemPosition = GetItemPosition(head.Value);
					if (_safePositionToJoin != null
					    && itemPosition.CompareTo(_safePositionToJoin.GetValueOrDefault()) <= 0
					    && itemPosition.CompareTo(minPosition) < 0) {
						minPosition = itemPosition;
						minStreamId = currentStreamId;
						anyEvent = true;
					}
				} else {
					anyDeletedStream = true;
					deletedStreamId = currentStreamId;
				}
			}

			if (!anyEvent && !anyDeletedStream) {
				if (!anyNonEmpty)
					DeliverSafePositionToJoin();
				break;
			}

			if (anyEvent) {
				var minHead = _buffers[minStreamId].Dequeue();
				if (minHead != null) {
					DeliverEvent(minHead.Value.Item1, minHead.Value.Item2);
				}

				if (_buffers[minStreamId].Count == 0)
					PauseOrContinueProcessing();
			}

			if (anyDeletedStream) {
				_buffers[deletedStreamId].Dequeue();
				SendPartitionDeleted_WhenReadingDataStream(deletedStreamId, null, null, null, null);
			}
		}
	}

	private void RequestEvents(string stream, bool delay) {
		if (_disposed)
			throw new InvalidOperationException("Disposed");
		if (PauseRequested || Paused)
			throw new InvalidOperationException("Paused or pause requested");

		if (_eventsRequested.Contains(stream))
			return;
		if (_buffers.TryGetValue(stream, out var queue) && queue.Count > 0)
			return;
		_eventsRequested.Add(stream);

		var pendingRequestCorrelationId = Guid.NewGuid();
		_pendingRequests[stream] = pendingRequestCorrelationId;

		var readEventsForward = new ClientMessage.ReadStreamEventsForward(
			Guid.NewGuid(), pendingRequestCorrelationId, new SendToThisEnvelope(this), stream,
			_fromPositions.Streams[stream],
			MaxReadCount, _resolveLinkTos, false, null, ReadAs, replyOnExpired: false);
		if (delay) {
			_publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					_publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition),
					CreateReadTimeoutMessage(pendingRequestCorrelationId, stream)));
			_publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					_publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition), readEventsForward));
		} else {
			_publisher.Publish(readEventsForward);
			ScheduleReadTimeoutMessage(pendingRequestCorrelationId, stream);
		}
	}

	private void ScheduleReadTimeoutMessage(Guid correlationId, string streamId) {
		_publisher.Publish(CreateReadTimeoutMessage(correlationId, streamId));
	}

	private Message CreateReadTimeoutMessage(Guid correlationId, string streamId) {
		return TimerMessage.Schedule.Create(
			TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout),
			new SendToThisEnvelope(this),
			new ProjectionManagementMessage.Internal.ReadTimeout(correlationId, streamId));
	}

	private void DeliverSafePositionToJoin() {
		if (_stopOnEof || _safePositionToJoin == null)
			return;
		// deliver if already available
		_publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId, null, PositionToSafeJoinPosition(_safePositionToJoin), 100.0f,
				source: GetType()));
	}

	private void UpdateSafePositionToJoin(string streamId, long? preparePosition) {
		_preparePositions[streamId] = preparePosition;
		if (_preparePositions.All(v => v.Value != null))
			_safePositionToJoin = _preparePositions.Min(v => v.Value.GetValueOrDefault());
	}

	private void DeliverEvent(KurrentDB.Core.Data.ResolvedEvent pair, float progress) {
		var positionEvent = pair.OriginalEvent;
		string streamId = positionEvent.EventStreamId;
		long fromPosition = _fromPositions.Streams[streamId];

		//if events have been deleted from the beginning of the stream, start from the first event we find
		if (fromPosition == 0 && positionEvent.EventNumber > 0) {
			fromPosition = positionEvent.EventNumber;
		}

		if (positionEvent.EventNumber != fromPosition) {
			// This can happen when the original stream has $maxAge/$maxCount set
			_publisher.Publish(new ReaderSubscriptionMessage.Faulted(EventReaderCorrelationId,
				$"Event number {fromPosition} was expected in the stream {streamId}, but event number {positionEvent.EventNumber} was received. This may happen if events have been deleted from the beginning of your stream, please reset your projection.", GetType()));
			return;
		}

		_fromPositions = _fromPositions.UpdateStreamPosition(streamId, positionEvent.EventNumber + 1);
		_publisher.Publish(
			//TODO: publish both link and event data
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId, new ResolvedEvent(pair, null),
				_stopOnEof ? null : positionEvent.LogPosition, progress, source: GetType()));
	}

	private static long? EventPairToPosition(KurrentDB.Core.Data.ResolvedEvent resolvedEvent) => resolvedEvent.OriginalEvent.LogPosition;

	private static long? MessageToLastCommitPosition(ClientMessage.ReadStreamEventsForwardCompleted message)
		=> GetLastCommitPositionFrom(message);

	private static long GetItemPosition((KurrentDB.Core.Data.ResolvedEvent, float) head)
		=> head.Item1.OriginalEvent.LogPosition;

	private static long GetMaxPosition() => long.MaxValue;

	private static long? PositionToSafeJoinPosition(long? safePositionToJoin) => safePositionToJoin;
}
