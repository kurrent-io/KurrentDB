// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using Serilog;

namespace EventStore.Projections.Core.Services.Processing.Subscriptions;

public class ReaderSubscriptionBase {
	private readonly IPublisher _publisher;
	private readonly IReaderStrategy _readerStrategy;
	private readonly ITimeProvider _timeProvider;
	private readonly long? _checkpointUnhandledBytesThreshold;
	private readonly int? _checkpointProcessedEventsThreshold;
	private readonly bool _stopOnEof;
	private readonly int? _stopAfterNEvents;
	protected readonly EventFilter _eventFilter;
	protected readonly PositionTagger _positionTagger;
	protected readonly PositionTracker _positionTracker;
	private long? _lastPassedOrCheckpointedEventPosition;
	private float _progress = -1;
	private long _subscriptionMessageSequenceNumber;
	private int _eventsSinceLastCheckpointSuggestedOrStart;
	private readonly Guid _subscriptionId;
	private bool _eofReached;
	protected string _tag;
	private TimeSpan _checkpointAfter;
	private DateTime _lastCheckpointTime = DateTime.MinValue;
	private bool _enableContentTypeValidation;
	private ILogger _logger;
	private CheckpointTag _lastCheckpointTag;

	protected ReaderSubscriptionBase(
		IPublisher publisher,
		Guid subscriptionId,
		CheckpointTag @from,
		IReaderStrategy readerStrategy,
		ITimeProvider timeProvider,
		long? checkpointUnhandledBytesThreshold,
		int? checkpointProcessedEventsThreshold,
		int checkpointAfterMs,
		bool stopOnEof,
		int? stopAfterNEvents,
		bool enableContentTypeValidation) {
		if (publisher == null) throw new ArgumentNullException("publisher");
		if (readerStrategy == null) throw new ArgumentNullException("readerStrategy");
		if (timeProvider == null) throw new ArgumentNullException("timeProvider");
		if (checkpointProcessedEventsThreshold > 0 && stopAfterNEvents > 0)
			throw new ArgumentException("checkpointProcessedEventsThreshold > 0 && stopAfterNEvents > 0");

		_publisher = publisher;
		_readerStrategy = readerStrategy;
		_timeProvider = timeProvider;
		_checkpointUnhandledBytesThreshold = checkpointUnhandledBytesThreshold;
		_checkpointProcessedEventsThreshold = checkpointProcessedEventsThreshold;
		_checkpointAfter = TimeSpan.FromMilliseconds(checkpointAfterMs);
		_stopOnEof = stopOnEof;
		_stopAfterNEvents = stopAfterNEvents;
		_subscriptionId = subscriptionId;
		_lastPassedOrCheckpointedEventPosition = null;

		_eventFilter = readerStrategy.EventFilter;

		_positionTagger = readerStrategy.PositionTagger;
		_positionTracker = new PositionTracker(_positionTagger);
		_positionTracker.UpdateByCheckpointTagInitial(@from);
		_lastCheckpointTag = _positionTracker.LastTag;
		_enableContentTypeValidation = enableContentTypeValidation;
		_logger = Serilog.Log.ForContext<ReaderSubscriptionBase>();
	}

	public string Tag {
		get { return _tag; }
	}

	public Guid SubscriptionId {
		get { return _subscriptionId; }
	}

	protected void ProcessOne(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		if (_eofReached)
			return; // eof may be set by reach N events

		// NOTE: we may receive here messages from heading event distribution point
		// and they may not pass out source filter.  Discard them first
		var roundedProgress = (float)Math.Round(message.Progress, 1);
		bool progressChanged = _progress != roundedProgress;

		bool passesStreamSourceFilter = _eventFilter.PassesSource(message.Data.ResolvedLinkTo, message.Data.PositionStreamId, message.Data.EventType);
		bool passesEventFilter = _eventFilter.Passes(message.Data.ResolvedLinkTo, message.Data.PositionStreamId, message.Data.EventType, message.Data.IsStreamDeletedEvent);
		bool isValid = !_enableContentTypeValidation || _eventFilter.PassesValidation(message.Data.IsJson, message.Data.DataMemory);
		if (!isValid) {
			_logger.Verbose($"Event {message.Data.EventSequenceNumber}@{message.Data.EventStreamId} is not valid json. Data: ({message.Data.Data})");
		}

		CheckpointTag eventCheckpointTag = null;

		if (passesStreamSourceFilter) {
			// NOTE: after joining heading distribution point it delivers all cached events to the subscription
			// some of this events we may have already received. The delivered events may have different order
			// (in case of partially ordered cases multi-stream reader etc). We discard all the messages that are not
			// after the last available checkpoint tag

			//NOTE: older events can appear here when replaying events from the heading event reader
			//      or when event-by-type-index reader reads TF and both event and resolved-event appear as output
			if (!_positionTagger.IsMessageAfterCheckpointTag(_positionTracker.LastTag, message))
				return;

			eventCheckpointTag = _positionTagger.MakeCheckpointTag(_positionTracker.LastTag, message);
			_positionTracker.UpdateByCheckpointTagForward(eventCheckpointTag);
		}

		var now = _timeProvider.UtcNow;
		var timeDifference = now - _lastCheckpointTime;
		if (isValid && passesEventFilter) {
			Debug.Assert(passesStreamSourceFilter, "Event passes event filter but not source filter");
			Debug.Assert(eventCheckpointTag != null, "Event checkpoint tag is null");

			_lastPassedOrCheckpointedEventPosition = message.Data.Position.PreparePosition;
			var convertedMessage =
				EventReaderSubscriptionMessage.CommittedEventReceived.FromCommittedEventDistributed(
					message, eventCheckpointTag, _eventFilter.GetCategory(message.Data.PositionStreamId),
					_subscriptionId, _subscriptionMessageSequenceNumber++);
			_publisher.Publish(convertedMessage);
			_eventsSinceLastCheckpointSuggestedOrStart++;
			if (_checkpointProcessedEventsThreshold > 0
			    && timeDifference > _checkpointAfter
			    && _eventsSinceLastCheckpointSuggestedOrStart >= _checkpointProcessedEventsThreshold
			    && _lastCheckpointTag != _positionTracker.LastTag)
				SuggestCheckpoint(message);
			if (_stopAfterNEvents > 0 && _eventsSinceLastCheckpointSuggestedOrStart >= _stopAfterNEvents)
				NEventsReached();
		} else {
			if (_checkpointUnhandledBytesThreshold > 0
			    && timeDifference > _checkpointAfter
			    && (_lastPassedOrCheckpointedEventPosition != null
			        && message.Data.Position.PreparePosition - _lastPassedOrCheckpointedEventPosition.Value
			        > _checkpointUnhandledBytesThreshold)
			    && _lastCheckpointTag != _positionTracker.LastTag)
				SuggestCheckpoint(message);
			else if (progressChanged)
				_progress = roundedProgress;
		}

		// initialize checkpointing based on first message
		if (_lastPassedOrCheckpointedEventPosition == null)
			_lastPassedOrCheckpointedEventPosition = message.Data.Position.PreparePosition;
	}

	private void NEventsReached() {
		ProcessEofAndEmitEof();
	}

	protected void NotifyProgress() {
		_publisher.Publish(new EventReaderSubscriptionMessage.ProgressChanged(
			_subscriptionId,
			_positionTracker.LastTag,
			_progress,
			_subscriptionMessageSequenceNumber++));
	}

	/// <summary>
	/// Forces a progression value.
	/// </summary>
	/// <param name="value">a percentage rate. For example if the progress is 42%, value parameter must be 42</param>
	protected void ForceProgressValue(float value) {
		_progress = value;
	}

	protected void PublishPartitionDeleted(string partition, CheckpointTag deletePosition) {
		_publisher.Publish(
			new EventReaderSubscriptionMessage.PartitionDeleted(
				_subscriptionId, deletePosition, partition, _subscriptionMessageSequenceNumber++));
	}

	private void PublishStartingAt(long startingLastCommitPosition) {
		_publisher.Publish(
			new EventReaderSubscriptionMessage.SubscriptionStarted(
				_subscriptionId, _positionTracker.LastTag, startingLastCommitPosition,
				_subscriptionMessageSequenceNumber++));
	}

	private void SuggestCheckpoint(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		_lastPassedOrCheckpointedEventPosition = message.Data.Position.PreparePosition;
		_lastCheckpointTag = _positionTracker.LastTag;
		_publisher.Publish(
			new EventReaderSubscriptionMessage.CheckpointSuggested(
				_subscriptionId, _positionTracker.LastTag, message.Progress,
				_subscriptionMessageSequenceNumber++));
		_eventsSinceLastCheckpointSuggestedOrStart = 0;
		_lastCheckpointTime = _timeProvider.UtcNow;
	}

	public IEventReader CreatePausedEventReader(IPublisher publisher, IODispatcher ioDispatcher,
		Guid eventReaderId) {
		if (_eofReached)
			throw new InvalidOperationException("Onetime projection has already reached the eof position");
//            _logger.Trace("Creating an event distribution point at '{lastTag}'", _positionTracker.LastTag);
		return _readerStrategy.CreatePausedEventReader(
			eventReaderId, publisher, ioDispatcher, _positionTracker.LastTag, _stopOnEof, _stopAfterNEvents);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderEof message) {
		if (_eofReached)
			return; // self eof-reached, but reader is still running

		if (_stopOnEof)
			ProcessEofAndEmitEof();
	}

	private void ProcessEofAndEmitEof() {
		_eofReached = true;
		EofReached();
		_publisher.Publish(
			new EventReaderSubscriptionMessage.EofReached(
				_subscriptionId,
				_positionTracker.LastTag,
				_subscriptionMessageSequenceNumber++));
		// self unsubscribe
		_publisher.Publish(new ReaderSubscriptionManagement.Unsubscribe(_subscriptionId));
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderPartitionEof message) {
		if (_eofReached)
			return; // self eof-reached, but reader is still running

		var eventCheckpointTag = _positionTagger.MakeCheckpointTag(_positionTracker.LastTag, message);

		_publisher.Publish(
			new EventReaderSubscriptionMessage.PartitionEofReached(
				_subscriptionId, eventCheckpointTag, message.Partition,
				_subscriptionMessageSequenceNumber++));
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderNotAuthorized message) {
		if (_eofReached)
			return; // self eof-reached, but reader is still running

		if (_stopOnEof) {
			_eofReached = true;
		}
		_publisher.Publish(
			new EventReaderSubscriptionMessage.NotAuthorized(
				_subscriptionId, _positionTracker.LastTag, _progress, _subscriptionMessageSequenceNumber++));
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderStarting message) {
		PublishStartingAt(message.LastCommitPosition);
	}

	protected virtual void EofReached() {
	}
}
