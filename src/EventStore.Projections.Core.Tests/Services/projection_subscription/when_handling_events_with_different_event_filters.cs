// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using EventStore.Core.Tests.Services.TimeService;

namespace EventStore.Projections.Core.Tests.Services.projection_subscription;

[TestFixture("$all", "good-event-type", "any-stream", "bad-event-type", true, true)]
[TestFixture("$all", "good-event-type", "any-stream", "bad-event-type", true, false)]
[TestFixture("$all", "good-event-type", "any-stream", "bad-event-type", false, true)]
[TestFixture("$all", "good-event-type", "any-stream", "bad-event-type", false, false)]
[TestFixture("$all", "good-event-type", "any-stream", "good-event-type", true, true)]
[TestFixture("$all", "good-event-type", "any-stream", "good-event-type", true, false)]
[TestFixture("$all", "good-event-type", "any-stream", "good-event-type", false, true)]
[TestFixture("$all", "good-event-type", "any-stream", "good-event-type", false, false)]
[TestFixture("good-stream", "good-event-type", "good-stream", "good-event-type", true, true)]
[TestFixture("good-stream", "good-event-type", "good-stream", "good-event-type", true, false)]
[TestFixture("good-stream", "good-event-type", "good-stream", "good-event-type", false, true)]
[TestFixture("good-stream", "good-event-type", "good-stream", "good-event-type", false, false)]
[TestFixture("good-stream", "good-event-type", "good-stream", "bad-event-type", true, true)]
[TestFixture("good-stream", "good-event-type", "good-stream", "bad-event-type", true, false)]
[TestFixture("good-stream", "good-event-type", "good-stream", "bad-event-type", false, true)]
[TestFixture("good-stream", "good-event-type", "good-stream", "bad-event-type", false, false)]
[TestFixture("good-stream", "good-event-type", "bad-stream", "good-event-type", false, true)]
[TestFixture("good-stream", "good-event-type", "bad-stream", "good-event-type", false, false)]
[TestFixture("good-stream", "good-event-type", "bad-stream", "bad-event-type", false, true)]
[TestFixture("good-stream", "good-event-type", "bad-stream", "bad-event-type", false, false)]
public class
	when_handling_events_with_different_event_filters :
		TestFixtureWithProjectionSubscription {
	private string _sourceStream;
	private string _sourceEventType;
	private string _stream;
	private string _eventType;
	private bool _exceedUnhandledBytesThreshold;
	private bool _checkpointIfEventsProcessed;
	private int _processedEvents;

	public when_handling_events_with_different_event_filters(
		string sourceStream, string sourceEventType, string stream, string eventType, bool exceedUnhandledBytesThreshold, bool checkpointIfEventsProcessed) {
		_sourceStream = sourceStream;
		_sourceEventType = sourceEventType;
		_stream = stream;
		_eventType = eventType;
		_exceedUnhandledBytesThreshold = exceedUnhandledBytesThreshold;
		_checkpointIfEventsProcessed = checkpointIfEventsProcessed;
		var passesEventFilter = (sourceStream == "$all" || stream == sourceStream) && eventType == sourceEventType;
		_processedEvents =  passesEventFilter ? 1 : 0;
	}

	protected override void Given() {
		_source = source => {
			if (_sourceStream == "$all") {
				source.FromAll();
			} else {
				source.FromStream(_sourceStream);
			}
			source.IncludeEvent(_sourceEventType);
		};
		_checkpointAfterMs = 1000;
		_checkpointProcessedEventsThreshold = _checkpointIfEventsProcessed ? 1 : 2;
		_checkpointUnhandledBytesThreshold = 4096;
		_timeProvider = new FakeTimeProvider();
	}

	protected override void When() {
		var firstPosition = new TFPos(200, 200);
		var offset = _exceedUnhandledBytesThreshold
			? (_checkpointUnhandledBytesThreshold + 1)
			: _checkpointUnhandledBytesThreshold;
		var secondPosition = new TFPos(firstPosition.CommitPosition + offset, firstPosition.PreparePosition + offset);

		//send a first event for checkpointing initialization if necessary
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), firstPosition, "a-random-stream-name", 1, false, Guid.NewGuid(),
				"a-random-event-type", false, new byte[0], new byte[0]));

		_checkpointHandler.HandledMessages.Clear();

		//send a second event to trigger checkpoint if necessary
		((FakeTimeProvider)_timeProvider).AddToUtcTime(TimeSpan.FromMilliseconds(_checkpointAfterMs + 1));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), secondPosition, _stream, 1, false, Guid.NewGuid(),
				_eventType, false, new byte[0], new byte[0]));
	}

	[Test]
	public void checkpoint_is_suggested_when_processed_events_threshold_exceeded_or_unhandled_bytes_threshold_exceeded() {
		var shouldCheckpoint = (_processedEvents >= _checkpointProcessedEventsThreshold) ||
		                       (_processedEvents == 0 && _exceedUnhandledBytesThreshold);
		Assert.AreEqual(shouldCheckpoint ? 1 : 0, _checkpointHandler.HandledMessages.Count);
	}
}
