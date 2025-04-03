// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Settings;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader
{
	private class TfBased : State,
		IHandle<ClientMessage.ReadAllEventsForwardCompleted>,
		IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
		private readonly HashSet<string> _eventTypes;
		private readonly ITimeProvider _timeProvider;
		private bool _tfEventsRequested;
		private bool _disposed;
		private readonly Dictionary<string, string> _streamToEventType;
		private readonly IPublisher _publisher;
		private TFPos _fromTfPosition;
		private bool _eof;
		private Guid _pendingRequestCorrelationId;

		public TfBased(
			ITimeProvider timeProvider, EventByTypeIndexEventReader reader, TFPos fromTfPosition,
			IPublisher publisher, ClaimsPrincipal readAs)
			: base(reader, readAs) {
			_timeProvider = timeProvider;
			_eventTypes = reader._eventTypes;
			_streamToEventType = _eventTypes.ToDictionary(v => "$et-" + v, v => v);
			_publisher = publisher;
			_fromTfPosition = fromTfPosition;
		}

		public void Handle(ClientMessage.ReadAllEventsForwardCompleted message) {
			if (_disposed)
				return;
			if (message.CorrelationId != _pendingRequestCorrelationId) {
				return;
			}

			if (message.Result == ReadAllResult.AccessDenied) {
				SendNotAuthorized();
				return;
			}

			if (!_tfEventsRequested)
				throw new InvalidOperationException("TF events has not been requested");
			if (_reader.Paused)
				throw new InvalidOperationException("Paused");
			_reader._lastPosition = message.TfLastCommitPosition;
			_tfEventsRequested = false;
			switch (message.Result) {
				case ReadAllResult.Success:
					var eof = message.Events is [];
					_eof = eof;
					var willDispose = _reader._stopOnEof && eof;
					_fromTfPosition = message.NextPos;

					if (!willDispose) {
						_reader.PauseOrContinueProcessing();
					}

					if (eof) {
						// the end
						//TODO: is it safe to pass NEXT as last commit position here
						DeliverLastCommitPosition(message.NextPos);
						// allow joining heading distribution
						SendIdle();
						_reader.SendEof();
					} else {
						foreach (var @event in message.Events) {
							var link = @event.Link;
							var data = @event.Event;
							var byStream = link != null && _streamToEventType.ContainsKey(link.EventStreamId);
							if (data == null)
								continue;
							var originalTfPosition = @event.OriginalPosition.Value;
							if (byStream) {
								// ignore data just update positions
								_reader.UpdateNextStreamPosition(link.EventStreamId, link.EventNumber + 1);
								// recover unresolved link event
								var unresolvedLinkEvent =
									EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(link,
										originalTfPosition.CommitPosition);
								DeliverEventRetrievedFromTf(
									unresolvedLinkEvent, 100.0f * link.LogPosition / message.TfLastCommitPosition,
									originalTfPosition);
							} else {
								DeliverEventRetrievedFromTf(
									@event, 100.0f * data.LogPosition / message.TfLastCommitPosition,
									originalTfPosition);
							}
						}
					}

					if (_disposed)
						return;

					break;
				default:
					throw new NotSupportedException(
						String.Format("ReadEvents result code was not recognized. Code: {0}", message.Result));
			}
		}

		public void Handle(ProjectionManagementMessage.Internal.ReadTimeout message) {
			if (_disposed) return;
			if (_reader.Paused) return;
			if (message.CorrelationId != _pendingRequestCorrelationId) return;

			_tfEventsRequested = false;
			_reader.PauseOrContinueProcessing();
		}

		private void RequestTfEvents(bool delay) {
			if (_disposed)
				throw new InvalidOperationException("Disposed");
			if (_reader.PauseRequested || _reader.Paused)
				throw new InvalidOperationException("Paused or pause requested");
			if (_tfEventsRequested)
				return;

			_tfEventsRequested = true;
			_pendingRequestCorrelationId = Guid.NewGuid();
			//TODO: we do not need resolve links, but lets check first with
			var readRequest = new ClientMessage.ReadAllEventsForward(
				_pendingRequestCorrelationId, _pendingRequestCorrelationId, new SendToThisEnvelope(this),
				_fromTfPosition.CommitPosition,
				_fromTfPosition.PreparePosition == -1 ? 0 : _fromTfPosition.PreparePosition,
				EventByTypeIndexEventReader.MaxReadCount,
				true, false, null, _readAs, replyOnExpired: false);

			var timeoutMessage = TimerMessage.Schedule.Create(
				TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout),
				new SendToThisEnvelope(this),
				new ProjectionManagementMessage.Internal.ReadTimeout(_pendingRequestCorrelationId, "$all"));

			_reader.PublishIORequest(delay, readRequest, timeoutMessage, _pendingRequestCorrelationId);
		}

		private void DeliverLastCommitPosition(TFPos lastPosition) {
			if (_reader._stopOnEof)
				return;
			_publisher.Publish(
				new ReaderSubscriptionMessage.CommittedEventDistributed(
					_reader.EventReaderCorrelationId, null, lastPosition.PreparePosition, 100.0f,
					source: this.GetType()));
			//TODO: check was is passed here
		}

		private void DeliverEventRetrievedFromTf(EventStore.Core.Data.ResolvedEvent pair, float progress,
			TFPos position) {
			var resolvedEvent = new ResolvedEvent(pair, null);

			DeliverEvent(progress, resolvedEvent, position, pair);
		}

		private void SendIdle() {
			_publisher.Publish(
				new ReaderSubscriptionMessage.EventReaderIdle(_reader.EventReaderCorrelationId, _timeProvider.UtcNow));
		}

		public override void Dispose() {
			_disposed = true;
		}

		public override void RequestEvents() {
			RequestTfEvents(delay: _eof);
		}

		public override bool AreEventsRequested() {
			return _tfEventsRequested;
		}
	}
}
