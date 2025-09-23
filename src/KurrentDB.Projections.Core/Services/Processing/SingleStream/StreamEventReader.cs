// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Settings;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Standard;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;

namespace KurrentDB.Projections.Core.Services.Processing.SingleStream;

public class StreamEventReader : EventReader,
	IHandle<ClientMessage.ReadStreamEventsForwardCompleted>,
	IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
	private readonly string _streamName;
	private long _fromSequenceNumber;
	private readonly ITimeProvider _timeProvider;
	private readonly bool _resolveLinkTos;
	private readonly bool _produceStreamDeletes;

	private bool _eventsRequested;
	private const int MaxReadCount = 111;
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
		ArgumentOutOfRangeException.ThrowIfNegative(fromSequenceNumber);
		ArgumentNullException.ThrowIfNull(streamName);
		ArgumentException.ThrowIfNullOrEmpty(streamName);
		_streamName = streamName;
		_fromSequenceNumber = fromSequenceNumber;
		_timeProvider = timeProvider;
		_resolveLinkTos = resolveLinkTos;
		_produceStreamDeletes = produceStreamDeletes;
	}

	protected override bool AreEventsRequested() => _eventsRequested;

	public void Handle(ClientMessage.ReadStreamEventsForwardCompleted message) {
		if (Disposed)
			return;
		if (!_eventsRequested)
			throw new InvalidOperationException("Read events has not been requested");
		if (message.EventStreamId != _streamName)
			throw new InvalidOperationException($"Invalid stream name: {message.EventStreamId}.  Expected: {_streamName}");
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
				SendPartitionDeleted_WhenReadingDataStream(_streamName, null, null, null, null);
				SendEof();
				break;
			case ReadStreamResult.NoStream:
				_eof = true;
				DeliverSafeJoinPosition(GetLastCommitPositionFrom(message)); // allow joining heading distribution
				PauseOrContinueProcessing();
				SendIdle();
				if (message.LastEventNumber >= 0)
					SendPartitionDeleted_WhenReadingDataStream(_streamName, null, null,
						null, null);
				SendEof();
				break;
			case ReadStreamResult.Success:
				var oldFromSequenceNumber = StartFrom(message, _fromSequenceNumber);
				_fromSequenceNumber = message.NextEventNumber;
				var eof = message.Events is [] && message.IsEndOfStream;
				_eof = eof;
				var willDispose = eof && StopOnEof;

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
						var link = message.Events[index].Link;
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
				throw new NotSupportedException($"ReadEvents result code was not recognized. Code: {message.Result}");
		}
	}

	public void Handle(ProjectionManagementMessage.Internal.ReadTimeout message) {
		if (Disposed)
			return;
		if (Paused)
			return;
		if (message.CorrelationId != _pendingRequestCorrelationId)
			return;

		_eventsRequested = false;
		PauseOrContinueProcessing();
	}

	private static long StartFrom(ClientMessage.ReadStreamEventsForwardCompleted message, long fromSequenceNumber) {
		if (fromSequenceNumber != 0)
			return fromSequenceNumber;
		return message.Events is not [] ? message.Events[0].OriginalEventNumber : fromSequenceNumber;
	}

	private void SendIdle() {
		Publisher.Publish(new ReaderSubscriptionMessage.EventReaderIdle(EventReaderCorrelationId, _timeProvider.UtcNow));
	}

	protected override void RequestEvents() {
		if (Disposed)
			throw new InvalidOperationException("Disposed");
		if (_eventsRequested)
			throw new InvalidOperationException("Read operation is already in progress");
		if (PauseRequested || Paused)
			throw new InvalidOperationException("Paused or pause requested");
		_eventsRequested = true;

		_pendingRequestCorrelationId = Guid.NewGuid();
		var readEventsForward = CreateReadEventsMessage(_pendingRequestCorrelationId);
		if (_eof) {
			Publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					Publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition),
					CreateReadTimeoutMessage(_pendingRequestCorrelationId, _streamName)));
			Publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					Publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition), readEventsForward));
		} else {
			Publisher.Publish(readEventsForward);
			ScheduleReadTimeoutMessage(_pendingRequestCorrelationId, _streamName);
		}
	}

	private void ScheduleReadTimeoutMessage(Guid correlationId, string streamId) {
		Publisher.Publish(CreateReadTimeoutMessage(correlationId, streamId));
	}

	private TimerMessage.Schedule CreateReadTimeoutMessage(Guid correlationId, string streamId) {
		return TimerMessage.Schedule.Create(
			TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout),
			new SendToThisEnvelope(this),
			new ProjectionManagementMessage.Internal.ReadTimeout(correlationId, streamId));
	}

	private ClientMessage.ReadStreamEventsForward CreateReadEventsMessage(Guid readCorrelationId) {
		return new(
			readCorrelationId, readCorrelationId, new SendToThisEnvelope(this), _streamName, _fromSequenceNumber,
			MaxReadCount, _resolveLinkTos, false, null, ReadAs, replyOnExpired: false);
	}

	private void DeliverSafeJoinPosition(long? safeJoinPosition) {
		if (StopOnEof || safeJoinPosition == null || safeJoinPosition == -1)
			return; //TODO: this should not happen, but StorageReader does not return it now
		Publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId, null, safeJoinPosition, 100.0f, source: GetType()));
	}

	private void DeliverEvent(KurrentDB.Core.Data.ResolvedEvent pair, float progress, ref long sequenceNumber) {
		EventRecord positionEvent = pair.OriginalEvent;
		if (positionEvent.EventNumber != sequenceNumber) {
			// This can happen when the original stream has $maxAge/$maxCount set
			Publisher.Publish(new ReaderSubscriptionMessage.Faulted(EventReaderCorrelationId,
				$"Event number {sequenceNumber} was expected in the stream {_streamName}, but event number {positionEvent.EventNumber} was received. This may happen if events have been deleted from the beginning of your stream, please reset your projection.", GetType()));
			return;
		}

		sequenceNumber = positionEvent.EventNumber + 1;
		var resolvedEvent = new ResolvedEvent(pair, null);

		if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
			return;

		bool isDeletedStreamEvent =
			StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(resolvedEvent, pair.ResolveResult, out var deletedPartitionStreamId);

		if (isDeletedStreamEvent) {
			if (_produceStreamDeletes)
				Publisher.Publish(
					//TODO: publish both link and event data
					new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
						EventReaderCorrelationId, deletedPartitionStreamId, source: GetType(),
						deleteEventOrLinkTargetPosition: null,
						deleteLinkOrEventPosition: resolvedEvent.EventOrLinkTargetPosition,
						positionStreamId: resolvedEvent.PositionStreamId,
						positionEventNumber: resolvedEvent.PositionSequenceNumber));
		} else if (!resolvedEvent.IsStreamDeletedEvent)
			Publisher.Publish(
				//TODO: publish both link and event data
				new ReaderSubscriptionMessage.CommittedEventDistributed(
					EventReaderCorrelationId, resolvedEvent, StopOnEof ? null : positionEvent.LogPosition,
					progress, source: GetType()));
	}
}
