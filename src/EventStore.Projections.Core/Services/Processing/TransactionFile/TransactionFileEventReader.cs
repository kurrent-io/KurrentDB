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

namespace EventStore.Projections.Core.Services.Processing.AllStream;

public class TransactionFileEventReader : EventReader,
	IHandle<ClientMessage.ReadAllEventsForwardCompleted>,
	IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
	private bool _eventsRequested;
	private int _maxReadCount = 250;
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
		TFPos @from,
		ITimeProvider timeProvider,
		bool stopOnEof = false,
		bool deliverEndOfTFPosition = true,
		bool resolveLinkTos = true)
		: base(publisher, eventReaderCorrelationId, readAs, stopOnEof) {
		if (publisher == null) throw new ArgumentNullException("publisher");
		_from = @from;
		_deliverEndOfTfPosition = deliverEndOfTFPosition;
		_resolveLinkTos = resolveLinkTos;
		_timeProvider = timeProvider;
	}

	protected override bool AreEventsRequested() {
		return _eventsRequested;
	}

	public void Handle(ClientMessage.ReadAllEventsForwardCompleted message) {
		if (_disposed)
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
		var willDispose = _stopOnEof && eof;
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
		if (_disposed) return;
		if (Paused) return;
		if (message.CorrelationId != _pendingRequestCorrelationId) return;

		_eventsRequested = false;
		PauseOrContinueProcessing();
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
					CreateReadTimeoutMessage(_pendingRequestCorrelationId, "$all")));
			_publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(
					_publisher, Guid.NewGuid(), null,
					new TFPos(_lastPosition, _lastPosition), readEventsForward));
		} else {
			_publisher.Publish(readEventsForward);
			ScheduleReadTimeoutMessage(_pendingRequestCorrelationId, "$all");
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

	private Message CreateReadEventsMessage(Guid correlationId) {
		return new ClientMessage.ReadAllEventsForward(
			correlationId, correlationId, new SendToThisEnvelope(this), _from.CommitPosition,
			_from.PreparePosition == -1 ? _from.CommitPosition : _from.PreparePosition, _maxReadCount,
			_resolveLinkTos, false, null, ReadAs, replyOnExpired: false);
	}

	private void DeliverLastCommitPosition(TFPos lastPosition) {
		if (_stopOnEof)
			return;
		_publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId, null, lastPosition.PreparePosition, 100.0f, source: this.GetType()));
		//TODO: check was is passed here
	}

	private void DeliverEvent(
		EventStore.Core.Data.ResolvedEvent @event, long lastCommitPosition, TFPos currentFrom) {
		EventRecord linkEvent = @event.Link;
		EventRecord targetEvent = @event.Event ?? linkEvent;
		EventRecord positionEvent = (linkEvent ?? targetEvent);

		TFPos receivedPosition = @event.OriginalPosition.Value;
		if (currentFrom > receivedPosition)
			throw new Exception(
				string.Format(
					"ReadFromTF returned events in incorrect order.  Last known position is: {0}.  Received position is: {1}",
					currentFrom, receivedPosition));

		var resolvedEvent = new ResolvedEvent(@event, null);

		string deletedPartitionStreamId;
		if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
			return;

		bool isDeletedStreamEvent = StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(
			resolvedEvent, @event.ResolveResult, out deletedPartitionStreamId);

		_publisher.Publish(
			new ReaderSubscriptionMessage.CommittedEventDistributed(
				EventReaderCorrelationId,
				resolvedEvent,
				_stopOnEof ? (long?)null : receivedPosition.PreparePosition,
				100.0f * positionEvent.LogPosition / lastCommitPosition,
				source: this.GetType()));
		if (isDeletedStreamEvent)
			_publisher.Publish(
				new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
					EventReaderCorrelationId, deletedPartitionStreamId, source: this.GetType(), lastEventNumber: -1,
					deleteEventOrLinkTargetPosition: resolvedEvent.EventOrLinkTargetPosition,
					deleteLinkOrEventPosition: resolvedEvent.LinkOrEventPosition,
					positionStreamId: positionEvent.EventStreamId, positionEventNumber: positionEvent.EventNumber));
	}
}
