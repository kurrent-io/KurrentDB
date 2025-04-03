// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.PersistentSubscription;

//TODO GFY REFACTOR TO USE ACTUAL STATE MACHINE
public class PersistentSubscription {
	private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscription>();

	public string SubscriptionId {
		get { return _settings.SubscriptionId; }
	}

	public IPersistentSubscriptionEventSource EventSource {
		get { return _settings.EventSource; }
	}

	public string GroupName {
		get { return _settings.GroupName; }
	}

	public bool ResolveLinkTos {
		get { return _settings.ResolveLinkTos; }
	}

	internal PersistentSubscriptionClientCollection _pushClients;
	private readonly PersistentSubscriptionStats _statistics;
	private readonly Stopwatch _totalTimeWatch;
	private readonly OutstandingMessageCache _outstandingMessages;
	private readonly TaskCompletionSource<StreamBuffer> _streamBufferSource;
	private PersistentSubscriptionState _state = PersistentSubscriptionState.NotReady;
	private IPersistentSubscriptionStreamPosition _nextEventToPullFrom;
	private bool _skipFirstEvent;
	private DateTime _lastCheckPointTime = DateTime.MinValue;
	private readonly PersistentSubscriptionParams _settings;
	private long _nextSequenceNumber;
	private long _lastCheckpointedSequenceNumber;
	private long _lastKnownSequenceNumber;
	private IPersistentSubscriptionStreamPosition _lastKnownMessage;
	private readonly object _lock = new object();
	private bool _faulted;

	public bool HasClients {
		get { return _pushClients.Count > 0; }
	}

	public int ClientCount {
		get { return _pushClients.Count; }
	}

	public bool HasReachedMaxClientCount {
		get { return _settings.MaxSubscriberCount != 0 && _pushClients.Count >= _settings.MaxSubscriberCount; }
	}

	public PersistentSubscriptionState State {
		get { return _state; }
	}

	public int OutstandingMessageCount {
		get { return _outstandingMessages.Count; } //use outstanding not connections as http shows up here too
	}

	public PersistentSubscription(PersistentSubscriptionParams persistentSubscriptionParams) {
		Ensure.NotNull(persistentSubscriptionParams.StreamReader, "eventLoader");
		Ensure.NotNull(persistentSubscriptionParams.CheckpointReader, "checkpointReader");
		Ensure.NotNull(persistentSubscriptionParams.CheckpointWriter, "checkpointWriter");
		Ensure.NotNull(persistentSubscriptionParams.MessageParker, "messageParker");
		Ensure.NotNull(persistentSubscriptionParams.SubscriptionId, "subscriptionId");
		Ensure.NotNull(persistentSubscriptionParams.EventSource, "eventSource");
		Ensure.NotNull(persistentSubscriptionParams.GroupName, "groupName");
		if (persistentSubscriptionParams.ReadBatchSize >= persistentSubscriptionParams.BufferSize) {
			throw new ArgumentOutOfRangeException($"{nameof(persistentSubscriptionParams.ReadBatchSize)} may not be greater than or equal to {nameof(persistentSubscriptionParams.BufferSize)}");
		}

		_totalTimeWatch = new Stopwatch();
		_settings = persistentSubscriptionParams;
		_nextEventToPullFrom = _settings.EventSource.StreamStartPosition;
		_totalTimeWatch.Start();
		_statistics = new PersistentSubscriptionStats(this, _settings, _totalTimeWatch);
		_outstandingMessages = new OutstandingMessageCache();
		_streamBufferSource = new TaskCompletionSource<StreamBuffer>(TaskCreationOptions.RunContinuationsAsynchronously);
		InitAsNew();
	}

	public void InitAsNew() {
		_state = PersistentSubscriptionState.NotReady;
		_nextSequenceNumber = 0L;
		_lastCheckpointedSequenceNumber = -1L;
		_lastKnownSequenceNumber = -1L;
		_lastKnownMessage = null;
		_statistics.SetLastKnownEventPosition(null);
		_settings.CheckpointReader.BeginLoadState(SubscriptionId, OnCheckpointLoaded);

		_pushClients = new PersistentSubscriptionClientCollection(_settings.ConsumerStrategy);
	}

	public bool TryGetStreamBuffer(out StreamBuffer streamBuffer) {
		if (_state == PersistentSubscriptionState.NotReady ||
			!_streamBufferSource.Task.IsCompletedSuccessfully) {
			streamBuffer = default;
			return false;
		}

		streamBuffer = _streamBufferSource.Task.Result;
		return true;
	}

	private void OnCheckpointLoaded(string checkpoint) {
		lock (_lock) {
			_state = PersistentSubscriptionState.Behind;
			if (checkpoint == null) {
				Log.Debug("Subscription {subscriptionId}: no checkpoint found", _settings.SubscriptionId);

				Log.Debug("Start from = " + _settings.StartFrom);

				_nextEventToPullFrom = _settings.StartFrom.IsLivePosition ? _settings.EventSource.StreamStartPosition : _settings.StartFrom;
				_streamBufferSource.SetResult(new StreamBuffer(_settings.BufferSize, _settings.LiveBufferSize, null,
				!_settings.StartFrom.IsLivePosition));
				TryReadingNewBatch();
			} else {
				_nextEventToPullFrom = _settings.EventSource.GetStreamPositionFor(checkpoint);
				_skipFirstEvent = true; //skip the checkpointed event

				//initialize values based on the loaded checkpoint
				_nextSequenceNumber = 1L;
				_lastCheckpointedSequenceNumber = 0L;
				_lastKnownSequenceNumber = 0L;
				_lastKnownMessage = _settings.EventSource.GetStreamPositionFor(checkpoint);
				_statistics.SetLastCheckPoint(_lastKnownMessage);

				Log.Debug("Subscription {subscriptionId}: read checkpoint {checkpoint}", _settings.SubscriptionId,
					checkpoint);
				_streamBufferSource.SetResult(new StreamBuffer(_settings.BufferSize, _settings.LiveBufferSize, _nextEventToPullFrom, true));
				_settings.MessageParker.BeginLoadStats(TryReadingNewBatch);
			}
		}
	}

	public void TryReadingNewBatch() {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				return;
			if ((_state & PersistentSubscriptionState.OutstandingPageRequest) > 0)
				return;
			if (streamBuffer.Live) {
				SetLive();
				return;
			}

			if (!streamBuffer.CanAccept(_settings.ReadBatchSize))
				return;
			_state |= PersistentSubscriptionState.OutstandingPageRequest;
			_settings.StreamReader.BeginReadEvents(_settings.EventSource, _nextEventToPullFrom,
				Math.Max(_settings.ReadBatchSize, 10), _settings.ReadBatchSize, _settings.MaxCheckPointCount,
				_settings.ResolveLinkTos, _skipFirstEvent, HandleReadCompleted, HandleSkippedEvents, HandleReadError);
			_skipFirstEvent = false;
		}
	}

	private void HandleReadError(string error) {
		lock (_lock) {
			_faulted = true;
			Log.Error("A non-recoverable error has occurred in persistent subscription {subscriptionId} : {error}. Dropping all connected clients.",
				_settings.SubscriptionId, error);
			var clients = _pushClients.GetAll().ToArray();
			foreach (var client in clients) {
				_pushClients.RemoveClientByCorrelationId(client.CorrelationId, true);
			}
		}
	}

	private void SetLive() {
		//TODO GFY this is hacky and just trying to keep the state at this level when it
		//lives in the streambuffer its for reporting reasons and likely should be revisited
		//at some point.
		_state &= ~PersistentSubscriptionState.Behind;
		_state |= PersistentSubscriptionState.Live;
	}

	private void SetBehind() {
		_state |= PersistentSubscriptionState.Behind;
		_state &= ~PersistentSubscriptionState.Live;
	}

	public void HandleReadCompleted(IReadOnlyList<ResolvedEvent> events, IPersistentSubscriptionStreamPosition newPosition, bool isEndOfStream) {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				return;
			if ((_state & PersistentSubscriptionState.OutstandingPageRequest) == 0)
				return;

			_state &= ~PersistentSubscriptionState.OutstandingPageRequest;

			if (streamBuffer.Live)
				return;
			foreach (var ev in events) {
				streamBuffer.AddReadMessage(OutstandingMessage.ForNewEvent(ev, _settings.EventSource.GetStreamPositionFor(ev)));
			}

			if (events.Count > 0) {
				_statistics.SetLastKnownEventPosition(_settings.EventSource.GetStreamPositionFor(events[^1]));
			}

			if (streamBuffer.Live) {
				SetLive();
			}

			if (isEndOfStream) {
				if (streamBuffer.TryMoveToLive()) {
					SetLive();
					return;
				}
			}

			_nextEventToPullFrom = newPosition;
			TryReadingNewBatch();
			TryPushingMessagesToClients();
		}
	}

	private void TryPushingMessagesToClients() {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				return;

			foreach (StreamBuffer.OutstandingMessagePointer messagePointer in streamBuffer.Scan()) {
				//optimistically assume that the message will be pushed
				//if it is, then we will increment the next sequence number if a new one was assigned
				//if it is not, then we will not increment the next sequence number
				(OutstandingMessage message, bool newSequenceNumberAssigned) =
					OutstandingMessage.ForPushedEvent(messagePointer.Message, _nextSequenceNumber, _lastKnownMessage);
				ConsumerPushResult result =
					_pushClients.PushMessageToClient(message);
				if (result == ConsumerPushResult.Sent) {
					messagePointer.MarkSent();

					if (newSequenceNumberAssigned) {
						//the message was pushed and a new sequence number was assigned
						//so we increment the next sequence number
						_nextSequenceNumber++;
					}

					MarkBeginProcessing(message);
				} else if (result == ConsumerPushResult.Skipped) {
					// The consumer strategy skipped the message so leave it in the buffer and continue.
				} else if (result == ConsumerPushResult.NoMoreCapacity) {
					return;
				}
			}
		}
	}

	public void NotifyLiveSubscriptionMessage(ResolvedEvent resolvedEvent) {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				return;
			if (_settings.EventSource.GetStreamPositionFor(resolvedEvent).CompareTo(_settings.StartFrom) < 0) {
				return;
			}

			if (_settings.EventSource.EventFilter != null && !_settings.EventSource.EventFilter.IsEventAllowed(resolvedEvent.Event)) {
				IPersistentSubscriptionStreamPosition position = new PersistentSubscriptionAllStreamPosition(-1, -1);
				if (resolvedEvent.OriginalPosition.HasValue) {
					position = new PersistentSubscriptionAllStreamPosition(
						resolvedEvent.OriginalPosition.Value.CommitPosition,
						resolvedEvent.OriginalPosition.Value.PreparePosition);
				}
				HandleSkippedEvents(position, 1);
				return;
			}

			_statistics.SetLastKnownEventPosition(_settings.EventSource.GetStreamPositionFor(resolvedEvent));
			var waslive = streamBuffer.Live; //hacky
			streamBuffer.AddLiveMessage(OutstandingMessage.ForNewEvent(resolvedEvent, _settings.EventSource.GetStreamPositionFor(resolvedEvent)));
			if (!streamBuffer.Live) {
				SetBehind();
				if (waslive)
					_nextEventToPullFrom = _settings.EventSource.GetStreamPositionFor(resolvedEvent);
			}

			TryPushingMessagesToClients();
		}
	}

	public IEnumerable<(ResolvedEvent ResolvedEvent, int RetryCount)> GetNextNOrLessMessages(int count) {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				yield break;

			foreach (var messagePointer in streamBuffer.Scan().Take(count)) {
				messagePointer.MarkSent();
				(OutstandingMessage message, bool newSequenceNumberAssigned) =
					OutstandingMessage.ForPushedEvent(messagePointer.Message, _nextSequenceNumber, _lastKnownMessage);
				if (newSequenceNumberAssigned) {
					_nextSequenceNumber++;
				}
				MarkBeginProcessing(message); //sequence number will be incremented in this call if a new one has been assigned
				yield return (messagePointer.Message.ResolvedEvent, messagePointer.Message.RetryCount);
			}
		}
	}

	private void MarkBeginProcessing(OutstandingMessage message) {
		_statistics.IncrementProcessed();
		if (message.EventSequenceNumber > _lastKnownSequenceNumber) {
			_lastKnownSequenceNumber = message.EventSequenceNumber.Value;
			_lastKnownMessage = _settings.EventSource.GetStreamPositionFor(message.ResolvedEvent);
		}

		StartMessage(message,
			_settings.MessageTimeout == TimeSpan.MaxValue
				? DateTime.MaxValue
				: DateTime.UtcNow + _settings.MessageTimeout);
	}

	public void AddClient(Guid correlationId, Guid connectionId, string connectionName, IEnvelope envelope, int maxInFlight, string user,
		string @from) {
		lock (_lock) {
			var client = new PersistentSubscriptionClient(correlationId, connectionId, connectionName, envelope, maxInFlight, user,
				@from, _totalTimeWatch, _settings.ExtraStatistics);
			_pushClients.AddClient(client);

			if (_faulted) {
				Log.Error("Dropping client: {clientId} from persistent subscription {subscriptionId} since the subscription has faulted.",
					client.CorrelationId, _settings.SubscriptionId);
				_pushClients.RemoveClientByCorrelationId(client.CorrelationId, true);
				return;
			}

			TryPushingMessagesToClients();
		}
	}

	public void Shutdown() {
		lock (_lock) {
			_pushClients.ShutdownAll();
		}
	}

	public void HandleSkippedEvents(IPersistentSubscriptionStreamPosition position, long skippedCount) {
		_lastKnownMessage = position;
		_nextSequenceNumber += skippedCount;
		_lastKnownSequenceNumber = _nextSequenceNumber - 1;
		TryMarkCheckpoint(false);
	}

	public bool RemoveClientByConnectionId(Guid connectionId) {
		lock (_lock) {
			if (!_pushClients.RemoveClientByConnectionId(connectionId,
				    out var unconfirmedEvents))
				return false;

			var lostMessages = unconfirmedEvents.OrderBy(v => v.ResolvedEvent.OriginalEventNumber);
			foreach (var m in lostMessages) {
				if (ActionTakenForRetriedMessage(m))
					return true;
				RetryMessage(m);
			}

			TryPushingMessagesToClients();
			return true;
		}
	}

	public void RemoveClientByCorrelationId(Guid correlationId, bool sendDropNotification) {
		lock (_lock) {
			var lostMessages = _pushClients.RemoveClientByCorrelationId(correlationId, sendDropNotification)
				.OrderBy(v => v.ResolvedEvent.OriginalEventNumber);
			foreach (var m in lostMessages) {
				RetryMessage(m);
			}

			TryPushingMessagesToClients();
		}
	}

	public void TryMarkCheckpoint(bool isTimeCheck) {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				return;

			OutstandingMessage? lowestMessage;
			long lowestSequenceNumber;

			(lowestMessage, lowestSequenceNumber)  = _outstandingMessages.GetLowestPosition();
			var (lowestRetryMessage, lowestRetrySequenceNumber) = streamBuffer.GetLowestRetry();

			if (lowestRetrySequenceNumber < lowestSequenceNumber) {
				lowestSequenceNumber = lowestRetrySequenceNumber;
				lowestMessage = lowestRetryMessage;
			}

			IPersistentSubscriptionStreamPosition lowestPosition;

			if (lowestSequenceNumber != long.MaxValue) {
				Debug.Assert(lowestMessage.HasValue);
				// Subtract one from retry and outstanding as those messages have not been acknowledged yet.
				lowestSequenceNumber--;
				lowestPosition = lowestMessage.Value.PreviousEventPosition;

				if (lowestPosition == null) {
					Debug.Assert(lowestSequenceNumber == -1L);
					//first message has been pushed but not yet acknowledged and we didn't have any previous checkpoint
					//so we have nothing to checkpoint yet
					return;
				}
				Debug.Assert(lowestPosition != null && lowestSequenceNumber >= 0L);
			} else {
				//no outstanding/retry messages. in this case we can say that the last known
				//event would be our checkpoint place (we have already completed it)
				lowestSequenceNumber = _lastKnownSequenceNumber;
				lowestPosition = _lastKnownMessage;
				Debug.Assert((lowestPosition != null && lowestSequenceNumber >= 0L) ||
				             (lowestPosition == null && lowestSequenceNumber == -1L));
			}

			if (lowestSequenceNumber == -1) //we have not even pushed any messages yet
				return;

			Debug.Assert(lowestPosition != null);

			var difference = lowestSequenceNumber - _lastCheckpointedSequenceNumber;
			var now = DateTime.UtcNow;
			var timedifference = now - _lastCheckPointTime;
			if (timedifference < _settings.CheckPointAfter && difference < _settings.MaxCheckPointCount)
				return;
			if ((difference >= _settings.MinCheckPointCount && isTimeCheck) ||
				difference >= _settings.MaxCheckPointCount) {
				_lastCheckPointTime = now;
				_lastCheckpointedSequenceNumber = lowestSequenceNumber;
				_settings.CheckpointWriter.BeginWriteState(lowestPosition);
				_statistics.SetLastCheckPoint(lowestPosition);
			}
		}
	}

	public void AcknowledgeMessagesProcessed(Guid correlationId, Guid[] processedEventIds) {
		lock (_lock) {
			RemoveProcessingMessages(processedEventIds);
			TryMarkCheckpoint(false);
			TryReadingNewBatch();
			TryPushingMessagesToClients();
		}
	}

	public void NotAcknowledgeMessagesProcessed(Guid correlationId, Guid[] processedEventIds, NakAction action,
		string reason) {
		lock (_lock) {
			foreach (var id in processedEventIds) {
				Log.Verbose("Message NAK'ed id {id} action to take {action} reason '{reason}'", id, action,
					reason ?? "");
				HandleNackedMessage(action, id, reason);
			}

			RemoveProcessingMessages(processedEventIds);
			TryMarkCheckpoint(false);
			TryReadingNewBatch();
			TryPushingMessagesToClients();
		}
	}

	private void HandleNackedMessage(NakAction action, Guid id, string reason) {
		OutstandingMessage m;
		switch (action) {
			case NakAction.Retry:
			case NakAction.Unknown:
				if (_outstandingMessages.GetMessageById(id, out m)) {
					if (!ActionTakenForRetriedMessage(m)) {
						RetryMessage(m);
					}
				}

				break;
			case NakAction.Park:
				if (_outstandingMessages.GetMessageById(id, out m)) {
					ParkMessage(m.ResolvedEvent, "Client explicitly NAK'ed message.\n" + reason, 0);
				}

				break;
			case NakAction.Stop:
				StopSubscription();
				break;
			case NakAction.Skip:
				SkipMessage(id);
				break;
			default:
				SkipMessage(id);
				break;
		}
	}

	private void ParkMessage(ResolvedEvent resolvedEvent, string reason, int count) {
		_settings.MessageParker.BeginParkMessage(resolvedEvent, reason, (e, result) => {
			if (result != OperationResult.Success) {
				if (count < 5) {
					Log.Information("Unable to park message {stream}/{eventNumber} operation failed {e} retrying",
						e.OriginalStreamId,
						e.OriginalEventNumber, result);
					ParkMessage(e, reason, count + 1);
					return;
				}

				Log.Error(
					"Unable to park message {stream}/{eventNumber} operation failed {e} after retries. Possible message loss",
					e.OriginalStreamId,
					e.OriginalEventNumber, result);
			}

			lock (_lock) {
				_outstandingMessages.Remove(e.OriginalEvent.EventId);
				_pushClients.RemoveProcessingMessages(e.OriginalEvent.EventId);
				TryPushingMessagesToClients();
			}
		});
	}


	public void RetryParkedMessages(long? stopAt) {
		lock (_lock) {
			if (_state == PersistentSubscriptionState.NotReady)
				return;
			if ((_state & PersistentSubscriptionState.ReplayingParkedMessages) > 0)
				return; //already replaying
			_state |= PersistentSubscriptionState.ReplayingParkedMessages;
			_settings.MessageParker.BeginReadEndSequence(end => {
				if (!end.HasValue) {
					_state ^= PersistentSubscriptionState.ReplayingParkedMessages;
					return; //nothing to do.
				}

				var stopRead = stopAt.HasValue ? Math.Min(stopAt.Value, end.Value + 1) : end.Value + 1;
				TryReadingParkedMessagesFrom(0,stopRead);
			});
		}
	}

	private void TryReadingParkedMessagesFrom(long position, long stopAt) {
		if ((_state & PersistentSubscriptionState.ReplayingParkedMessages) == 0)
			return; //not replaying

		Ensure.Positive(stopAt - position, "count");

		var count = (int)Math.Min((stopAt + 1) - position, _settings.ReadBatchSize); // Reading an extra event to get the Timestamp for the stopAt EventNumber
		_settings.StreamReader.BeginReadEvents(
			new PersistentSubscriptionSingleStreamEventSource(_settings.ParkedMessageStream),
			new PersistentSubscriptionSingleStreamPosition(position),
			count,
			_settings.ReadBatchSize, _settings.MaxCheckPointCount, true, false,
			(events, newposition, isstop) => HandleParkedReadCompleted(events, newposition, isstop, stopAt),
			(_, _) => { }, HandleParkedReadError);
	}

	private void HandleParkedReadError(string error) {
		Log.Error("Error reading parked messages from: {parkedStream} for persistent subscription {subscriptionId} : {error}",
		_settings.ParkedMessageStream, _settings.SubscriptionId, error);
	}

	public void HandleParkedReadCompleted(IReadOnlyList<ResolvedEvent> events, IPersistentSubscriptionStreamPosition newPosition, bool isEndofStream, long stopAt) {
		lock (_lock) {
			if (!TryGetStreamBuffer(out var streamBuffer))
				return;
			if ((_state & PersistentSubscriptionState.ReplayingParkedMessages) == 0)
				return;

			foreach (var ev in events) {
				if (ev.OriginalEventNumber == stopAt) {
					break;
				}

				if (ev.Event is null) {
					continue;
				}

				Log.Debug("Replaying parked message: {eventId} {stream}/{eventNumber} on subscription {subscriptionId}",
					ev.OriginalEvent.EventId, ev.OriginalStreamId, ev.OriginalEventNumber,
					_settings.SubscriptionId);
				streamBuffer.AddRetry(OutstandingMessage.ForParkedEvent(ev));
			}

			TryPushingMessagesToClients();

			var newStreamPosition = newPosition as PersistentSubscriptionSingleStreamPosition;
			Ensure.NotNull(newStreamPosition, "newStreamPosition");

			if (isEndofStream || stopAt <= newStreamPosition.StreamEventNumber) {
				var replayedEnd = newStreamPosition.StreamEventNumber == -1 ? stopAt : Math.Min(stopAt, newStreamPosition.StreamEventNumber);

				if (isEndofStream) {
					_settings.MessageParker.BeginMarkParkedMessagesReprocessed(replayedEnd, null, true);
				} else {
					var (replayedEndTimeStamp, updateOldestParkedMessageTimeStamp) = GetOldestParkedMessageTimeStamp(events, replayedEnd);
					_settings.MessageParker.BeginMarkParkedMessagesReprocessed(replayedEnd, replayedEndTimeStamp, updateOldestParkedMessageTimeStamp);
				}

				_state ^= PersistentSubscriptionState.ReplayingParkedMessages;
			}else {
				TryReadingParkedMessagesFrom(newStreamPosition.StreamEventNumber, stopAt);
			}
		}
	}

	private static (DateTime?, bool) GetOldestParkedMessageTimeStamp(IReadOnlyList<ResolvedEvent> events, long replayedEnd) {
		foreach (var resolvedEvent in events) {
			if (resolvedEvent.OriginalEvent.EventNumber != replayedEnd) continue;
				return (resolvedEvent.OriginalEvent.TimeStamp, true);
		}

		return (null, false);
	}

	private void StartMessage(OutstandingMessage message, DateTime expires) {
		var result = _outstandingMessages.StartMessage(message, expires);

		if (result == StartMessageResult.SkippedDuplicate) {
			Log.Warning("Skipping message {stream}/{eventNumber} (stream position={streamPosition}) with duplicate eventId {eventId}",
				message.ResolvedEvent.OriginalStreamId,
				message.ResolvedEvent.OriginalEventNumber,
				_settings.EventSource.GetStreamPositionFor(message.ResolvedEvent).ToString(),
				message.EventId);
		}
	}

	private void SkipMessage(Guid id) {
		_outstandingMessages.Remove(id);
	}

	private void StopSubscription() {
		//TODO CC Stop subscription?
	}

	private void RemoveProcessingMessages(Guid[] processedEventIds) {
		_pushClients.RemoveProcessingMessages(processedEventIds);
		foreach (var id in processedEventIds) {
			_outstandingMessages.Remove(id);
		}
	}

	public void NotifyClockTick(DateTime time) {
		lock (_lock) {
			if (_state == PersistentSubscriptionState.NotReady)
				return;

			foreach (var message in _outstandingMessages.GetMessagesExpiringBefore(time)) {
				if (!ActionTakenForRetriedMessage(message)) {
					RetryMessage(message);
				}
			}

			TryPushingMessagesToClients();
			TryMarkCheckpoint(true);
			if ((_state & PersistentSubscriptionState.Behind |
				 PersistentSubscriptionState.OutstandingPageRequest) ==
				PersistentSubscriptionState.Behind)
				TryReadingNewBatch();
		}
	}

	private bool ActionTakenForRetriedMessage(OutstandingMessage message) {
		if (message.RetryCount < _settings.MaxRetryCount)
			return false;
		ParkMessage(message.ResolvedEvent, string.Format("Reached retry count of {0}", _settings.MaxRetryCount), 0);
		return true;
	}

	private void RetryMessage(OutstandingMessage message) {
		if (!TryGetStreamBuffer(out var streamBuffer))
			return;

		var @event = message.ResolvedEvent;
		if (!message.IsReplayedEvent) {
			Log.Debug("Retrying message {subscriptionId} {stream}/{eventNumber} (stream position={streamPosition})",
				SubscriptionId,
				@event.OriginalStreamId, @event.OriginalEventNumber,
				_settings.EventSource.GetStreamPositionFor(message.ResolvedEvent).ToString());
		} else {
			Log.Debug("Retrying parked message: {eventId} {stream}/{eventNumber} on subscription {subscriptionId}",
				@event.OriginalEvent.EventId, @event.OriginalStreamId, @event.OriginalEventNumber,
				_settings.SubscriptionId);
		}

		_outstandingMessages.Remove(@event.OriginalEvent.EventId);
		_pushClients.RemoveProcessingMessages(@event.OriginalEvent.EventId);
		streamBuffer.AddRetry(OutstandingMessage.ForRetriedEvent(message));
	}

	public MonitoringMessage.PersistentSubscriptionInfo GetStatistics() {
		return _statistics.GetStatistics();
	}

	public void RetrySingleParkedMessage(ResolvedEvent @event) {
		if (!TryGetStreamBuffer(out var streamBuffer))
			return;

		streamBuffer.AddRetry(OutstandingMessage.ForParkedEvent(@event));
	}

	public void Delete() {
		_settings.CheckpointWriter.BeginDelete(x => { });
		_settings.MessageParker.BeginDelete(x => { });
	}
}
