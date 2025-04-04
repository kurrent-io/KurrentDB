using System;
using System.Collections.Generic;
using System.Linq;
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

namespace EventStore.Projections.Core.Services.Processing {
	public class EventByTypeIndexEventReader : EventReader {
		public const int MaxReadCount = 50;
		private readonly HashSet<string> _eventTypes;
		private readonly bool _resolveLinkTos;
		private readonly bool _includeDeletedStreamNotification;
		private readonly ITimeProvider _timeProvider;

		private class PendingEvent {
			public readonly EventStore.Core.Data.ResolvedEvent ResolvedEvent;
			public readonly float Progress;
			public readonly TFPos TfPosition;

			public PendingEvent(EventStore.Core.Data.ResolvedEvent resolvedEvent, TFPos tfPosition, float progress) {
				ResolvedEvent = resolvedEvent;
				Progress = progress;
				TfPosition = tfPosition;
			}
		}

		private State _state;
		private TFPos _lastEventPosition;
		private readonly Dictionary<string, long> _fromPositions;
		private readonly Dictionary<string, string> _streamToEventType;
		private long _lastPosition;

		public EventByTypeIndexEventReader(
			IPublisher publisher,
			Guid eventReaderCorrelationId,
			ClaimsPrincipal readAs,
			string[] eventTypes,
			bool includeDeletedStreamNotification,
			TFPos fromTfPosition,
			Dictionary<string, long> fromPositions,
			bool resolveLinkTos,
			ITimeProvider timeProvider,
			bool stopOnEof = false)
			: base(publisher, eventReaderCorrelationId, readAs, stopOnEof) {
			if (eventTypes == null)
				throw new ArgumentNullException("eventTypes");
			if (timeProvider == null)
				throw new ArgumentNullException("timeProvider");
			if (eventTypes.Length == 0)
				throw new ArgumentException("empty", "eventTypes");

			_includeDeletedStreamNotification = includeDeletedStreamNotification;
			_timeProvider = timeProvider;
			_eventTypes = new HashSet<string>(eventTypes);
			if (includeDeletedStreamNotification)
				_eventTypes.Add("$deleted");
			_streamToEventType = eventTypes.ToDictionary(v => "$et-" + v, v => v);
			_lastEventPosition = fromTfPosition;
			_resolveLinkTos = resolveLinkTos;

			ValidateTag(fromPositions);

			_fromPositions = fromPositions;
			_state = new IndexBased(_eventTypes, this, readAs);
		}

		private void ValidateTag(Dictionary<string, long> fromPositions) {
			if (_eventTypes.Count != fromPositions.Count)
				throw new ArgumentException("Number of streams does not match", "fromPositions");

			foreach (var stream in _streamToEventType.Keys.Where(stream => !fromPositions.ContainsKey(stream))) {
				throw new ArgumentException(
					String.Format("The '{0}' stream position has not been set", stream), "fromPositions");
			}
		}


		public override void Dispose() {
			_state.Dispose();
			base.Dispose();
		}

		protected override void RequestEvents() {
			if (_disposed || PauseRequested || Paused)
				return;
			_state.RequestEvents();
		}

		protected override bool AreEventsRequested() {
			return _state.AreEventsRequested();
		}

		private void PublishIORequest(bool delay, Message readEventsForward, Message timeoutMessage,
			Guid correlationId) {
			if (delay) {
				_publisher.Publish(
					new AwakeServiceMessage.SubscribeAwake(
						new PublishEnvelope(_publisher, crossThread: true), correlationId, null,
						new TFPos(_lastPosition, _lastPosition), readEventsForward));
			} else {
				_publisher.Publish(readEventsForward);
				_publisher.Publish(timeoutMessage);
			}
		}

		private void UpdateNextStreamPosition(string eventStreamId, long nextPosition) {
			long streamPosition;
			if (!_fromPositions.TryGetValue(eventStreamId, out streamPosition))
				streamPosition = -1;
			if (nextPosition > streamPosition)
				_fromPositions[eventStreamId] = nextPosition;
		}

		private abstract class State : IDisposable {
			public abstract void RequestEvents();
			public abstract bool AreEventsRequested();
			public abstract void Dispose();

			protected readonly EventByTypeIndexEventReader _reader;
			protected readonly ClaimsPrincipal _readAs;

			protected State(EventByTypeIndexEventReader reader, ClaimsPrincipal readAs) {
				_reader = reader;
				_readAs = readAs;
			}

			protected void DeliverEvent(float progress, ResolvedEvent resolvedEvent, TFPos position,
				EventStore.Core.Data.ResolvedEvent pair) {
				if (resolvedEvent.EventOrLinkTargetPosition <= _reader._lastEventPosition)
					return;
				_reader._lastEventPosition = resolvedEvent.EventOrLinkTargetPosition;
				//TODO: this is incomplete.  where reading from TF we need to handle actual deletes

				string deletedPartitionStreamId;


				if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
					return;

				bool isDeletedStreamEvent = StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(
					resolvedEvent, pair.ResolveResult, out deletedPartitionStreamId);
				if (isDeletedStreamEvent) {
					var deletedPartition = deletedPartitionStreamId;

					if (_reader._includeDeletedStreamNotification)
						_reader._publisher.Publish(
							//TODO: publish both link and event data
							new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
								_reader.EventReaderCorrelationId, deletedPartition, source: this.GetType(),
								lastEventNumber: -1, deleteEventOrLinkTargetPosition: position,
								deleteLinkOrEventPosition: resolvedEvent.EventOrLinkTargetPosition,
								positionStreamId: resolvedEvent.PositionStreamId,
								positionEventNumber: resolvedEvent.PositionSequenceNumber));
				} else
					_reader._publisher.Publish(
						//TODO: publish both link and event data
						new ReaderSubscriptionMessage.CommittedEventDistributed(
							_reader.EventReaderCorrelationId, resolvedEvent,
							_reader._stopOnEof ? (long?)null : position.PreparePosition, progress,
							source: this.GetType()));
			}

			protected void SendNotAuthorized() {
				_reader.SendNotAuthorized();
			}
		}

		private class IndexBased : State,
			IHandle<ClientMessage.ReadStreamEventsForwardCompleted>,
			IHandle<ClientMessage.ReadStreamEventsBackwardCompleted>,
			IHandle<ProjectionManagementMessage.Internal.ReadTimeout> {
			private readonly Dictionary<string, string> _streamToEventType;
			private readonly HashSet<string> _eventsRequested = new HashSet<string>();
			private readonly HashSet<Guid> _validRequests = new HashSet<Guid>();
			private bool _indexCheckpointStreamRequested;
			private long _lastKnownIndexCheckpointEventNumber = -1;
			private TFPos? _lastKnownIndexCheckpointPosition = null;

			private readonly Dictionary<string, Queue<PendingEvent>> _buffers =
				new Dictionary<string, Queue<PendingEvent>>();

			private readonly Dictionary<string, bool> _eofs;
			private bool _disposed;
			private bool _indexStreamEof = false;
			private readonly IPublisher _publisher;

			private readonly Dictionary<string, Guid> _pendingRequests;
			private readonly object _lock = new object();

			public IndexBased(HashSet<string> eventTypes, EventByTypeIndexEventReader reader, ClaimsPrincipal readAs)
				: base(reader, readAs) {
				_streamToEventType = eventTypes.ToDictionary(v => "$et-" + v, v => v);
				_eofs = _streamToEventType.Keys.ToDictionary(v => v, v => false);
				// whatever the first event returned is (even if we start from the same position as the last processed event
				// let subscription handle this 
				_publisher = _reader._publisher;

				_pendingRequests = new Dictionary<string, Guid>();
				_pendingRequests.Add("$et", Guid.Empty);
				foreach (var stream in _streamToEventType.Keys) {
					_pendingRequests.Add(stream, Guid.Empty);
				}
			}


			private TFPos GetTargetEventPosition(PendingEvent head) {
				return head.TfPosition;
			}

			public void Handle(ClientMessage.ReadStreamEventsForwardCompleted message) {
				if (_disposed)
					return;
				if (message.Result == ReadStreamResult.AccessDenied) {
					SendNotAuthorized();
					return;
				}

				//we may receive read replies in any order (we read multiple streams)
				if (message.TfLastCommitPosition > _reader._lastPosition)
					_reader._lastPosition = message.TfLastCommitPosition;
				if (message.EventStreamId == "$et") {
					ReadIndexCheckpointStreamCompleted(message.Result, message.Events);
					return;
				}

				if (!_validRequests.Contains(message.CorrelationId))
					return;

				lock (_lock) {
					if (!_pendingRequests.Values.Any(x => x == message.CorrelationId))
						return;
				}

				if (!_streamToEventType.ContainsKey(message.EventStreamId))
					throw new InvalidOperationException(
						String.Format("Invalid stream name: {0}", message.EventStreamId));
				if (!_eventsRequested.Contains(message.EventStreamId))
					throw new InvalidOperationException("Read events has not been requested");
				if (_reader.Paused)
					throw new InvalidOperationException("Paused");
				switch (message.Result) {
					case ReadStreamResult.NoStream:
						_eofs[message.EventStreamId] = true;
						ProcessBuffersAndContinue(eventStreamId: message.EventStreamId);
						break;
					case ReadStreamResult.Success:
						_reader.UpdateNextStreamPosition(message.EventStreamId, message.NextEventNumber);
						_eofs[message.EventStreamId] = (message.Events.Length == 0) && message.IsEndOfStream;
						EnqueueEvents(message);
						ProcessBuffersAndContinue(eventStreamId: message.EventStreamId);
						break;
					default:
						throw new NotSupportedException(
							String.Format("ReadEvents result code was not recognized. Code: {0}", message.Result));
				}
			}

			public void Handle(ProjectionManagementMessage.Internal.ReadTimeout message) {
				if (_disposed)
					return;
				if (_reader.Paused)
					return;
				lock (_lock) {
					if (!_pendingRequests.Values.Any(x => x == message.CorrelationId))
						return;
				}

				if (message.StreamId == "$et") {
					_indexCheckpointStreamRequested = false;
				}

				_eventsRequested.Remove(message.StreamId);
				_reader.PauseOrContinueProcessing();
			}

			private void ProcessBuffersAndContinue(string eventStreamId) {
				ProcessBuffers();
				if (eventStreamId != null)
					_eventsRequested.Remove(eventStreamId);
				_reader.PauseOrContinueProcessing();
				CheckSwitch();
			}

			private void CheckSwitch() {
				if (ShouldSwitch()) {
					Dispose();
					_reader.DoSwitch(_lastKnownIndexCheckpointPosition.Value);
				}
			}

			public void Handle(ClientMessage.ReadStreamEventsBackwardCompleted message) {
				if (_disposed)
					return;
				//we may receive read replies in any order (we read multiple streams)
				if (message.TfLastCommitPosition > _reader._lastPosition)
					_reader._lastPosition = message.TfLastCommitPosition;
				if (message.Result == ReadStreamResult.AccessDenied) {
					SendNotAuthorized();
					return;
				}

				ReadIndexCheckpointStreamCompleted(message.Result, message.Events);
			}

			private void EnqueueEvents(ClientMessage.ReadStreamEventsForwardCompleted message) {
				for (int index = 0; index < message.Events.Length; index++) {
					var @event = message.Events[index].Event;
					var @link = message.Events[index].Link;
					EventRecord positionEvent = (link ?? @event);
					var queue = GetStreamQueue(positionEvent);
					//TODO: progress calculation below is incorrect.  sum(current)/sum(last_event) where sum by all streams
					var tfPosition =
						positionEvent.Metadata.ParseCheckpointTagJson().Position;
					var progress = 100.0f * (link ?? @event).EventNumber / message.LastEventNumber;
					var pendingEvent = new PendingEvent(message.Events[index], tfPosition, progress);
					queue.Enqueue(pendingEvent);
				}
			}

			private Queue<PendingEvent> GetStreamQueue(EventRecord positionEvent) {
				Queue<PendingEvent> queue;
				if (!_buffers.TryGetValue(positionEvent.EventStreamId, out queue)) {
					queue = new Queue<PendingEvent>();
					_buffers.Add(positionEvent.EventStreamId, queue);
				}

				return queue;
			}

			private bool BeforeTheLastKnownIndexCheckpoint(TFPos tfPosition) {
				return _lastKnownIndexCheckpointPosition != null && tfPosition <= _lastKnownIndexCheckpointPosition;
			}

			private void ReadIndexCheckpointStreamCompleted(
				ReadStreamResult result, EventStore.Core.Data.ResolvedEvent[] events) {
				if (_disposed)
					return;

				if (!_indexCheckpointStreamRequested)
					throw new InvalidOperationException("Read index checkpoint has not been requested");
				if (_reader.Paused)
					throw new InvalidOperationException("Paused");
				_indexCheckpointStreamRequested = false;
				switch (result) {
					case ReadStreamResult.NoStream:
						_indexStreamEof = true;
						_lastKnownIndexCheckpointPosition = default(TFPos);
						ProcessBuffersAndContinue(null);
						break;
					case ReadStreamResult.Success:
						if (events.Length == 0) {
							_indexStreamEof = true;
							if (_lastKnownIndexCheckpointPosition == null)
								_lastKnownIndexCheckpointPosition = default(TFPos);
						} else {
							_indexStreamEof = false;
							//NOTE: only one event if backward order was requested
							foreach (var @event in events) {
								var data = @event.Event.Data.ParseCheckpointTagJson();
								_lastKnownIndexCheckpointEventNumber = @event.Event.EventNumber;
								_lastKnownIndexCheckpointPosition = data.Position;
								// reset eofs before this point - probably some where updated so we cannot go 
								// forward with this position without making sure nothing appeared
								// NOTE: performance is not very good, but we should switch to TF mode shortly


								foreach (var corrId in _validRequests) {
									_publisher.Publish(new AwakeServiceMessage.UnsubscribeAwake(corrId));
								}

								_validRequests.Clear();
								_eventsRequested.Clear();
								//TODO: cancel subscribeAwake
								//TODO: reissue read requests
								//TODO: make sure async completions of awake do not work

								foreach (var key in _eofs.Keys.ToArray())
									_eofs[key] = false;
							}
						}

						ProcessBuffersAndContinue(null);
						break;
					default:
						throw new NotSupportedException(
							String.Format("ReadEvents result code was not recognized. Code: {0}", result));
				}
			}

			private void ProcessBuffers() {
				if (_disposed) // max N reached
					return;
				while (true) {
					var minStreamId = "";
					var minPosition = new TFPos(Int64.MaxValue, Int64.MaxValue);
					var any = false;
					var anyEof = false;
					foreach (var streamId in _streamToEventType.Keys) {
						Queue<PendingEvent> buffer;
						_buffers.TryGetValue(streamId, out buffer);

						if ((buffer == null || buffer.Count == 0))
							if (_eofs[streamId]) {
								anyEof = true;
								continue; // eof - will check if it was safe later
							} else
								return; // still reading

						var head = buffer.Peek();
						var targetEventPosition = GetTargetEventPosition(head);

						if (targetEventPosition < minPosition) {
							minPosition = targetEventPosition;
							minStreamId = streamId;
							any = true;
						}
					}

					if (!any)
						break;

					if (!anyEof || BeforeTheLastKnownIndexCheckpoint(minPosition)) {
						var minHead = _buffers[minStreamId].Dequeue();
						DeliverEventRetrievedByIndex(minHead.ResolvedEvent, minHead.Progress, minPosition);
					} else
						return; // no safe events to deliver

					if (_buffers[minStreamId].Count == 0)
						_reader.PauseOrContinueProcessing();
				}
			}

			private void RequestCheckpointStream(bool delay) {
				if (_disposed)
					throw new InvalidOperationException("Disposed");
				if (_reader.PauseRequested || _reader.Paused)
					throw new InvalidOperationException("Paused or pause requested");
				if (_indexCheckpointStreamRequested)
					return;

				_indexCheckpointStreamRequested = true;

				var pendingRequestCorrelationId = Guid.NewGuid();
				lock (_lock) {
					_pendingRequests["$et"] = pendingRequestCorrelationId;
				}

				Message readRequest;
				if (_lastKnownIndexCheckpointEventNumber == -1) {
					readRequest = new ClientMessage.ReadStreamEventsBackward(
						pendingRequestCorrelationId, pendingRequestCorrelationId, new SendToThisEnvelope(this), "$et",
						-1, 1, false, false, null,
						_readAs);
				} else {
					readRequest = new ClientMessage.ReadStreamEventsForward(
						pendingRequestCorrelationId, pendingRequestCorrelationId, new SendToThisEnvelope(this), "$et",
						_lastKnownIndexCheckpointEventNumber + 1, 100, false, false, null, _readAs, replyOnExpired: false);
				}

				var timeoutMessage = TimerMessage.Schedule.Create(
					TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout),
					new SendToThisEnvelope(this),
					new ProjectionManagementMessage.Internal.ReadTimeout(pendingRequestCorrelationId, "$et"));

				_reader.PublishIORequest(delay, readRequest, timeoutMessage, pendingRequestCorrelationId);
			}

			private void RequestEvents(string stream, bool delay) {
				if (_disposed)
					throw new InvalidOperationException("Disposed");
				if (_reader.PauseRequested || _reader.Paused)
					throw new InvalidOperationException("Paused or pause requested");

				if (_eventsRequested.Contains(stream))
					return;
				Queue<PendingEvent> queue;
				if (_buffers.TryGetValue(stream, out queue) && queue.Count > 0)
					return;
				_eventsRequested.Add(stream);

				var corrId = Guid.NewGuid();
				_validRequests.Add(corrId);

				lock (_lock) {
					_pendingRequests[stream] = corrId;
				}

				var readEventsForward = new ClientMessage.ReadStreamEventsForward(
					corrId, corrId, new SendToThisEnvelope(this), stream,
					_reader._fromPositions[stream], EventByTypeIndexEventReader.MaxReadCount, _reader._resolveLinkTos,
					false, null,
					_readAs,
					replyOnExpired: false);

				var timeoutMessage = TimerMessage.Schedule.Create(
					TimeSpan.FromMilliseconds(ESConsts.ReadRequestTimeout),
					new SendToThisEnvelope(this),
					new ProjectionManagementMessage.Internal.ReadTimeout(corrId, stream));

				_reader.PublishIORequest(delay, readEventsForward, timeoutMessage, corrId);
			}

			private void DeliverEventRetrievedByIndex(EventStore.Core.Data.ResolvedEvent pair, float progress,
				TFPos position) {
				//TODO: add event sequence validation for inside the index stream
				var resolvedEvent = new ResolvedEvent(pair, null);
				DeliverEvent(progress, resolvedEvent, position, pair);
			}

			public override bool AreEventsRequested() {
				return _eventsRequested.Count != 0 || _indexCheckpointStreamRequested;
			}

			public override void Dispose() {
				_disposed = true;
			}

			public override void RequestEvents() {
				foreach (var stream in _streamToEventType.Keys)
					RequestEvents(stream, delay: _eofs[stream]);
				RequestCheckpointStream(delay: _indexStreamEof);
			}

			private bool ShouldSwitch() {
				if (_disposed)
					return false;
				if (_reader.Paused || _reader.PauseRequested)
					return false;
				Queue<PendingEvent> q;
				var shouldSwitch = _lastKnownIndexCheckpointPosition != null
								   && _streamToEventType.Keys.All(
									   v =>
										   _eofs[v]
										   || _buffers.TryGetValue(v, out q) && q.Count > 0
																			 &&
																			 !BeforeTheLastKnownIndexCheckpoint(
																				 q.Peek().TfPosition));
				return shouldSwitch;
			}
		}

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
						var eof = message.Events.Length == 0;
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
				if (_disposed)
					return;
				if (_reader.Paused)
					return;
				if (message.CorrelationId != _pendingRequestCorrelationId)
					return;

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

		private void DoSwitch(TFPos lastKnownIndexCheckpointPosition) {
			if (Paused || PauseRequested || _disposed)
				throw new InvalidOperationException("_paused || _pauseRequested || _disposed");

			// skip reading TF up to last know index checkpoint position 
			// as we could only gethere if there is no more indexed events before this point
			if (lastKnownIndexCheckpointPosition > _lastEventPosition)
				_lastEventPosition = lastKnownIndexCheckpointPosition;

			_state = new TfBased(_timeProvider, this, _lastEventPosition, this._publisher, ReadAs);
			_state.RequestEvents();
		}
	}
}
