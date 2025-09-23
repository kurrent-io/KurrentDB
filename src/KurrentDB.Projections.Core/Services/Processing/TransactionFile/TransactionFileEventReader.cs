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

namespace KurrentDB.Projections.Core.Services.Processing.TransactionFile;

public class TransactionFileEventReader
	: EventReader,
		IHandle<ClientMessage.ReadAllEventsForwardCompleted>,
		IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
	private bool _eventsRequested;
	private const int MaxReadCount = 250;
	private TFPos _from;
	private readonly bool _deliverEndOfTfPosition;
	private readonly bool _resolveLinkTos;
	private readonly ITimeProvider _timeProvider;
	private long _lastPosition;
	private bool _eof;
	private Guid _pendingRequestCorrelationId;

	public TransactionFileEventReader(
		IPublisher publisher,
		Guid eventReaderCorrelationId,
		ClaimsPrincipal readAs,
		TFPos from,
		ITimeProvider timeProvider,
		bool stopOnEof = false,
		bool deliverEndOfTFPosition = true,
		bool resolveLinkTos = true)
		: base(publisher, eventReaderCorrelationId, readAs, stopOnEof) {
		ArgumentNullException.ThrowIfNull(publisher);
		_from = from;
		_deliverEndOfTfPosition = deliverEndOfTFPosition;
		_resolveLinkTos = resolveLinkTos;
		_timeProvider = timeProvider;
	}

	protected override bool AreEventsRequested() => _eventsRequested;

	public void Handle(ClientMessage.ReadAllEventsForwardCompleted message) {
		if (Disposed)
			return;
		if (!_eventsRequested)
			throw new InvalidOperationException("Read events has not been requested");
		if (Paused)
			throw new InvalidOperationException("Paused");
		if (message.CorrelationId != _pendingRequestCorrelationId) {
			return;
		}

		_eventsRequested = false;
		_lastPosition = message.TfLastCommitPosition;
		if (message.Result == ReadAllResult.AccessDenied) {
			SendNotAuthorized();
			return;
		}

		var eof = message.Events is [] && message.IsEndOfStream;
		_eof = eof;
		var willDispose = StopOnEof && eof;
		var oldFrom = _from;
		_from = message.NextPos;

		if (!willDispose) {
			PauseOrContinueProcessing();
		}

		if (eof) {
			// the end
			if (_deliverEndOfTfPosition)
				DeliverLastCommitPosition(_from);
			// allow joining heading distribution
			SendIdle();
			SendEof();
		} else {
			for (int index = 0; index < message.Events.Count; index++) {
				var @event = message.Events[index];
				DeliverEvent(@event, message.TfLastCommitPosition, oldFrom);
			}
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
					CreateReadTimeoutMessage(_pendingRequestCorrelationId, "$all")));
			Publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					Publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition), readEventsForward));
		} else {
			Publisher.Publish(readEventsForward);
			ScheduleReadTimeoutMessage(_pendingRequestCorrelationId, "$all");
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

	private ClientMessage.ReadAllEventsForward CreateReadEventsMessage(Guid correlationId) {
		return new(
			correlationId, correlationId, new SendToThisEnvelope(this), _from.CommitPosition,
			_from.PreparePosition == -1 ? _from.CommitPosition : _from.PreparePosition, MaxReadCount,
			_resolveLinkTos, false, null, ReadAs, replyOnExpired: false);
	}

	private void DeliverLastCommitPosition(TFPos lastPosition) {
		if (StopOnEof)
			return;
		Publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId, null, lastPosition.PreparePosition, 100.0f, source: GetType()));
		//TODO: check was is passed here
	}

	private void DeliverEvent(KurrentDB.Core.Data.ResolvedEvent @event, long lastCommitPosition, TFPos currentFrom) {
		EventRecord linkEvent = @event.Link;
		EventRecord targetEvent = @event.Event ?? linkEvent;
		EventRecord positionEvent = linkEvent ?? targetEvent;

		TFPos receivedPosition = @event.OriginalPosition.Value;
		if (currentFrom > receivedPosition)
			throw new Exception(
				$"ReadFromTF returned events in incorrect order.  Last known position is: {currentFrom}.  Received position is: {receivedPosition}");

		var resolvedEvent = new ResolvedEvent(@event, null);

		if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
			return;

		bool isDeletedStreamEvent = StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(
			resolvedEvent, @event.ResolveResult, out var deletedPartitionStreamId);

		Publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId,
				resolvedEvent,
				StopOnEof ? null : receivedPosition.PreparePosition,
				100.0f * positionEvent.LogPosition / lastCommitPosition,
				source: GetType()));
		if (isDeletedStreamEvent)
			Publisher.Publish(
				new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
					EventReaderCorrelationId, deletedPartitionStreamId, source: GetType(),
					deleteEventOrLinkTargetPosition: resolvedEvent.EventOrLinkTargetPosition,
					deleteLinkOrEventPosition: resolvedEvent.LinkOrEventPosition,
					positionStreamId: positionEvent.EventStreamId, positionEventNumber: positionEvent.EventNumber));
	}
}
