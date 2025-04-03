// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Services;
using EventStore.Core.Tests.ClientAPI.Helpers;
using NUnit.Framework;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;
using ResolvedEvent = EventStore.ClientAPI.ResolvedEvent;
using StreamMetadata = EventStore.ClientAPI.StreamMetadata;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class read_all_events_filtered_paging_should<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private List<EventData> _testEvents = new List<EventData>();

	private List<EventData> _testEventsA;

	private List<EventData> _testEventsC;

	public read_all_events_filtered_paging_should() : base(chunkSize: 2 * 1024 * 1024) { }

	protected override async Task When() {
		await _conn.SetStreamMetadataAsync("$all", -1,
			StreamMetadata.Build().SetReadRole(SystemRoles.All),
			DefaultData.AdminCredentials);

		_testEventsA = Enumerable
			.Range(0, 10)
			.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "AEvent"))
			.ToList();

		var testEventsB = Enumerable
			.Range(0, 10000)
			.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "BEvent"))
			.ToList();

		_testEventsC = Enumerable
			.Range(0, 10)
			.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "CEvent"))
			.ToList();

		_testEvents.AddRange(_testEventsA);
		_testEvents.AddRange(testEventsB);
		_testEvents.AddRange(_testEventsC);

		_conn.AppendToStreamAsync("stream-a", ExpectedVersion.NoStream, _testEvents).Wait();
	}

	[Test, Category("LongRunning")]
	public void handle_paging_between_events_forward() {
		var numberOfEmptySlicesRead = 0;

		var filter = Filter.EventType.Prefix("CE");
		var sliceStart = Position.Start;
		var read = new List<ResolvedEvent>();
		AllEventsSlice slice;

		do {
			slice = _conn.FilteredReadAllEventsForwardAsync(sliceStart, 50, false, filter, maxSearchWindow: 100)
				.GetAwaiter()
				.GetResult();

			if (slice.Events.Length == 0) {
				numberOfEmptySlicesRead++;
			} else {
				read.AddRange(slice.Events);
			}

			sliceStart = slice.NextPosition;
		} while (!slice.IsEndOfStream);

		Assert.That(EventDataComparer.Equal(
			_testEventsC.ToArray(),
			read.Select(x => x.Event).ToArray()));

		Assert.AreEqual(100, numberOfEmptySlicesRead);
	}

	[Test, Category("LongRunning")]
	public void handle_paging_between_events_backward() {
		var numberOfEmptySlicesRead = 0;

		var filter = Filter.EventType.Prefix("AE");
		var sliceStart = Position.End;
		var read = new List<ResolvedEvent>();
		AllEventsSlice slice;

		do {
			slice = _conn.FilteredReadAllEventsBackwardAsync(sliceStart, 50, false, filter, maxSearchWindow: 100)
				.GetAwaiter()
				.GetResult();
			if (slice.Events.Length == 0) {
				numberOfEmptySlicesRead++;
			} else {
				read.AddRange(slice.Events);
			}

			sliceStart = slice.NextPosition;
		} while (!slice.IsEndOfStream);

		Assert.That(EventDataComparer.Equal(
			_testEventsA.ReverseEvents(),
			read.Select(x => x.Event).ToArray()));

		Assert.AreEqual(100, numberOfEmptySlicesRead);
	}

	[Test, Category("LongRunning")]
	public void handle_paging_between_events_returns_correct_number_of_events_for_max_search_window_forward() {
		var filter = Filter.EventType.Prefix("BE");

		var slice = _conn.FilteredReadAllEventsForwardAsync(Position.Start, 30, false, filter, maxSearchWindow: 30)
			.GetAwaiter()
			.GetResult();

		// Includes system events at start of stream (inc epoch-information)
		var expectedCount = 11;

		if (LogFormatHelper<TLogFormat, TStreamId>.IsV3) {
			// account for stream records: $scavenges, $user-admin, $user-ops, $users, stream-a
			expectedCount -= 5;
			// account for event type records: $UserCreated, $User, AEvent, BEvent, CEvent
			expectedCount -= 5;
		}

		Assert.AreEqual(expectedCount, slice.Events.Length);
	}

	[Test, Category("LongRunning")]
	public void handle_paging_between_events_returns_correct_number_of_events_for_max_search_window_backward() {
		var filter = Filter.EventType.Prefix("BE");

		var slice = _conn.FilteredReadAllEventsBackwardAsync(Position.End, 20, false, filter, maxSearchWindow: 20)
			.GetAwaiter()
			.GetResult();

		Assert.AreEqual(10, slice.Events.Length); // Includes system events
	}
}
