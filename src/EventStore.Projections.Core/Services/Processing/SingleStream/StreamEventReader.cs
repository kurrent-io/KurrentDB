// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Settings;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Standard;

namespace EventStore.Projections.Core.Services.Processing.SingleStream;

public class StreamEventReader : EventReader,
	IHandle<ClientMessage.ReadStreamEventsForwardCompleted>,
	IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
	private readonly string _streamName;
	private long _fromSequenceNumber;
	private readonly ITimeProvider _timeProvider;
	private readonly bool _resolveLinkTos;
	private readonly bool _produceStreamDeletes;

	private bool _eventsRequested;
	private int _maxReadCount = 111;
	private long _lastPosition;
	private bool _eof;
	private Guid _pendingRequestCorrelationId;

	public StreamEventReader(
		IPublisher publisher,
		Guid eventReaderCorrelationId,
		ClaimsPrincipal readAs,
		string streamName,
		long fromSequenceNumber,
		ITimeProvider timeProvider,
		bool resolveLinkTos,
		bool produceStreamDeletes,
		bool stopOnEof = false)
		: base(publisher, eventReaderCorrelationId, readAs, stopOnEof) {
		if (fromSequenceNumber < 0) throw new ArgumentException("fromSequenceNumber");
		if (streamName == null) throw new ArgumentNullException("streamName");
		if (string.IsNullOrEmpty(streamName)) throw new ArgumentException("streamName");
		_streamName = streamName;
		_fromSequenceNumber = fromSequenceNumber;
		_timeProvider = timeProvider;
		_resolveLinkTos = resolveLinkTos;
		_produceStreamDeletes = produceStreamDeletes;
	}

	protected override bool AreEventsRequested() {
		return _eventsRequested;
	}

	public void Handle(ClientMessage.ReadStreamEventsForwardCompleted message) {
		if (_disposed)
			return;
		if (!_eventsRequested)
			throw new InvalidOperationException("Read events has not been requested");
		if (message.EventStreamId != _streamName)
			throw new InvalidOperationException(
				string.Format("Invalid stream name: {0}.  Expected: {1}", message.EventStreamId, _streamName));
		if (Paused)
			throw new InvalidOperationException("Paused");
		if (message.CorrelationId != _pendingRequestCorrelationId) {
			return;
		}

		_eventsRequested = false;
		_lastPosition = message.TfLastCommitPosition;
		NotifyIfStarting(message.TfLastCommitPosition);
		switch (message.Result) {
			case ReadStreamResult.StreamDeleted:
				_eof = true;
				DeliverSafeJoinPosition(GetLastCommitPositionFrom(message)); // allow joining heading distribution
				PauseOrContinueProcessing();
				SendIdle();
				SendPartitionDeleted_WhenReadingDataStream(_streamName, -1, null, null, null, null);
				SendEof();
				break;
			case ReadStreamResult.NoStream:
				_eof = true;
				DeliverSafeJoinPosition(GetLastCommitPositionFrom(message)); // allow joining heading distribution
				PauseOrContinueProcessing();
				SendIdle();
				if (message.LastEventNumber >= 0)
					SendPartitionDeleted_WhenReadingDataStream(_streamName, message.LastEventNumber, null, null,
						null, null);
				SendEof();
				break;
			case ReadStreamResult.Success:
				var oldFromSequenceNumber = StartFrom(message, _fromSequenceNumber);
				_fromSequenceNumber = message.NextEventNumber;
				var eof = message.Events is [] && message.IsEndOfStream;
				_eof = eof;
				var willDispose = eof && _stopOnEof;

				if (!willDispose) {
					PauseOrContinueProcessing();
				}

				if (eof) {
					// the end
					DeliverSafeJoinPosition(GetLastCommitPositionFrom(message));
					SendIdle();
					SendEof();
				} else {
					for (int index = 0; index < message.Events.Count; index++) {
						var @event = message.Events[index].Event;
						var @link = message.Events[index].Link;
						DeliverEvent(message.Events[index],
							100.0f * (link ?? @event).EventNumber / message.LastEventNumber,
							ref oldFromSequenceNumber);
					}
				}

				break;
			case ReadStreamResult.AccessDenied:
				SendNotAuthorized();
				return;
			default:
				throw new NotSupportedException(
					string.Format("ReadEvents result code was not recognized. Code: {0}", message.Result));
		}
	}

	public void Handle(ProjectionManagementMessage.Internal.ReadTimeout message) {
		if (_disposed) return;
		if (Paused) return;
		if (message.CorrelationId != _pendingRequestCorrelationId) return;

		_eventsRequested = false;
		PauseOrContinueProcessing();
	}

	private long StartFrom(ClientMessage.ReadStreamEventsForwardCompleted message, long fromSequenceNumber) {
		if (fromSequenceNumber != 0) return fromSequenceNumber;
		if (message.Events is not []) {
			return message.Events[0].OriginalEventNumber;
		}

		return fromSequenceNumber;
	}

	private void SendIdle() {
		_publisher.Publish(
			new ReaderSubscriptionMessage.EventReaderIdle(EventReaderCorrelationId, _timeProvider.UtcNow));
	}

	protected override void RequestEvents() {
		if (_disposed) throw new InvalidOperationException("Disposed");
		if (_eventsRequested)
			throw new InvalidOperationException("Read operation is already in progress");
		if (PauseRequested || Paused)
			throw new InvalidOperationException("Paused or pause requested");
		_eventsRequested = true;

		_pendingRequestCorrelationId = Guid.NewGuid();
		var readEventsForward = CreateReadEventsMessage(_pendingRequestCorrelationId);
		if (_eof) {
			_publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					_publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition),
					CreateReadTimeoutMessage(_pendingRequestCorrelationId, _streamName)));
			_publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					_publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition), readEventsForward));
		} else {
			_publisher.Publish(readEventsForward);
			ScheduleReadTimeoutMessage(_pendingRequestCorrelationId, _streamName);
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

	private Message CreateReadEventsMessage(Guid readCorrelationId) {
		return new ClientMessage.ReadStreamEventsForward(
			readCorrelationId, readCorrelationId, new SendToThisEnvelope(this), _streamName, _fromSequenceNumber,
			_maxReadCount, _resolveLinkTos, false, null, ReadAs, replyOnExpired: false);
	}

	private void DeliverSafeJoinPosition(long? safeJoinPosition) {
		if (_stopOnEof || safeJoinPosition == null || safeJoinPosition == -1)
			return; //TODO: this should not happen, but StorageReader does not return it now
		_publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId, null, safeJoinPosition, 100.0f, source: this.GetType()));
	}

	private void DeliverEvent(EventStore.Core.Data.ResolvedEvent pair, float progress, ref long sequenceNumber) {
		EventRecord positionEvent = pair.OriginalEvent;
		if (positionEvent.EventNumber != sequenceNumber) {
			// This can happen when the original stream has $maxAge/$maxCount set
			_publisher.Publish(new ReaderSubscriptionMessage.Faulted(EventReaderCorrelationId, string.Format(
				"Event number {0} was expected in the stream {1}, but event number {2} was received. This may happen if events have been deleted from the beginning of your stream, please reset your projection.",
				sequenceNumber, _streamName, positionEvent.EventNumber), this.GetType()));
			return;
		}

		sequenceNumber = positionEvent.EventNumber + 1;
		var resolvedEvent = new ResolvedEvent(pair, null);

		string deletedPartitionStreamId;

		if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
			return;

		bool isDeletedStreamEvent =
			StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(resolvedEvent, pair.ResolveResult,
				out deletedPartitionStreamId);

		if (isDeletedStreamEvent) {
			var deletedPartition = deletedPartitionStreamId;

			if (_produceStreamDeletes)
				_publisher.Publish(
					//TODO: publish both link and event data
					new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
						EventReaderCorrelationId, deletedPartition, source: this.GetType(), lastEventNumber: -1,
						deleteEventOrLinkTargetPosition: null,
						deleteLinkOrEventPosition: resolvedEvent.EventOrLinkTargetPosition,
						positionStreamId: resolvedEvent.PositionStreamId,
						positionEventNumber: resolvedEvent.PositionSequenceNumber));
		} else if (!resolvedEvent.IsStreamDeletedEvent)
			_publisher.Publish(
				//TODO: publish both link and event data
				new ReaderSubscriptionMessage.CommittedEventDistributed(
					EventReaderCorrelationId, resolvedEvent, _stopOnEof ? (long?)null : positionEvent.LogPosition,
					progress, source: this.GetType()));
	}
}
