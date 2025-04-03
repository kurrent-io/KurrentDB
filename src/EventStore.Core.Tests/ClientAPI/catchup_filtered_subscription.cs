// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services;

namespace EventStore.Core.Tests.ClientAPI;

[Category("LongRunning"), Category("ClientAPI")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class catchup_filtered_subscription<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private MiniNode<TLogFormat, TStreamId> _node;
	private IEventStoreConnection _conn;
	private List<EventData> _testEvents;
	private List<EventData> _testEventsAfter;
	private const int Timeout = 10000;

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();

		_conn = BuildConnection(_node);
		await _conn.ConnectAsync();
		_conn.SetStreamMetadataAsync(SystemStreams.AllStream, -1,
			StreamMetadata.Build().SetReadRole(SystemRoles.All),
			new UserCredentials(SystemUsers.Admin, SystemUsers.DefaultAdminPassword)).Wait();

		_testEvents = Enumerable
			.Range(0, 10)
			.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "AEvent"))
			.ToList();

		_testEvents.AddRange(
			Enumerable
				.Range(0, 10)
				.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "BEvent")).ToList());

		_testEventsAfter = Enumerable
			.Range(0, 10)
			.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "AEvent"))
			.ToList();

		_testEventsAfter.AddRange(
			Enumerable
				.Range(0, 10)
				.Select(x => TestEvent.NewTestEvent(x.ToString(), eventName: "BEvent")).ToList());

		await _conn.AppendToStreamAsync("stream-a", ExpectedVersion.NoStream, _testEvents.EvenEvents());
		await _conn.AppendToStreamAsync("stream-b", ExpectedVersion.NoStream, _testEvents.OddEvents());
	}

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[Test]
	public void calls_checkpoint_delegate_during_catchup() {
		var filter = Filter.StreamId.Prefix("stream-a");
		// in v2 there are 20 events, 10 in stream-a and 10 in stream-b.
		// in v3 there are additionally two stream records and two event type records
		var isV2 = LogFormatHelper<TLogFormat, TStreamId>.IsV2;
		var checkpointReached = new CountdownEvent(isV2 ? 10 : 14);
		var eventsSeen = 0;

		var settings = new CatchUpSubscriptionFilteredSettings(
			maxLiveQueueSize: 10000,
			readBatchSize: 2,
			verboseLogging: false,
			resolveLinkTos: true,
			maxSearchWindow: 2,
			subscriptionName: String.Empty
		);

		_conn.FilteredSubscribeToAllFrom(
			Position.Start,
			filter,
			settings,
			(s, e) => {
				eventsSeen++;
				return Task.CompletedTask;
			},
			(s, p) => {
				checkpointReached.Signal();
				return Task.CompletedTask;
			}, 1);

		if (!checkpointReached.Wait(Timeout)) {
			Assert.Fail("Checkpoint reached not called enough times within time limit.");
		}

		Assert.AreEqual(10, eventsSeen);
	}

	[Test]
	public void calls_checkpoint_during_live_processing_stage() {
		var filter = Filter.StreamId.Prefix("stream-a");
		var appeared = new CountdownEvent(_testEventsAfter.EvenEvents().Count + 1); // Calls once for switch to live.
		var eventsSeen = 0;
		var isLive = false;

		var settings = new CatchUpSubscriptionFilteredSettings(
			10000,
			1,
			verboseLogging: false,
			resolveLinkTos: true,
			maxSearchWindow: 1,
			subscriptionName: String.Empty
		);

		_conn.FilteredSubscribeToAllFrom(
			Position.Start,
			filter,
			settings,
			(s, e) => {
				eventsSeen++;
				return Task.CompletedTask;
			},
			(s, p) => {
				if (isLive) {
					appeared.Signal();
				}

				return Task.CompletedTask;
			}, 1, s => {
				isLive = true;
				_conn.AppendToStreamAsync("stream-a", ExpectedVersion.Any, _testEventsAfter.EvenEvents()).Wait();
			});

		if (!appeared.Wait(Timeout)) {
			Assert.Fail("Checkpoint appeared not called enough times within time limit.");
		}

		Assert.AreEqual(20, eventsSeen);
	}

	[Test, Category("LongRunning")]
	public void only_return_events_with_a_given_stream_prefix() {
		var filter = Filter.StreamId.Prefix("stream-a");
		var foundEvents = new ConcurrentBag<ResolvedEvent>();
		var appeared = new CountdownEvent(20);

		Subscribe(filter, foundEvents, appeared);

		if (!appeared.Wait(Timeout)) {
			Assert.Fail("Appeared countdown event timed out.");
		}

		Assert.True(foundEvents.All(e => e.Event.EventStreamId == "stream-a"));
	}

	[Test, Category("LongRunning")]
	public void only_return_events_with_a_given_event_prefix() {
		var filter = Filter.EventType.Prefix("AE");
		var foundEvents = new ConcurrentBag<ResolvedEvent>();
		var appeared = new CountdownEvent(20);

		Subscribe(filter, foundEvents, appeared);

		if (!appeared.Wait(Timeout)) {
			Assert.Fail("Appeared countdown event timed out.");
		}

		Assert.True(foundEvents.All(e => e.Event.EventType == "AEvent"));
	}

	[Test, Category("LongRunning")]
	public void only_return_events_that_satisfy_a_given_stream_regex() {
		var filter = Filter.StreamId.Regex(new Regex(@"^.*eam-b.*$"));
		var foundEvents = new ConcurrentBag<ResolvedEvent>();
		var appeared = new CountdownEvent(20);

		Subscribe(filter, foundEvents, appeared);

		if (!appeared.Wait(Timeout)) {
			Assert.Fail("Appeared countdown event timed out.");
		}

		Assert.True(foundEvents.All(e => e.Event.EventStreamId == "stream-b"));
	}

	[Test, Category("LongRunning")]
	public void only_return_events_that_satisfy_a_given_event_regex() {
		var filter = Filter.EventType.Regex(new Regex(@"^.*BEv.*$"));
		var foundEvents = new ConcurrentBag<ResolvedEvent>();
		var appeared = new CountdownEvent(20);

		Subscribe(filter, foundEvents, appeared);

		if (!appeared.Wait(Timeout)) {
			Assert.Fail("Appeared countdown event timed out.");
		}

		Assert.True(foundEvents.All(e => e.Event.EventType == "BEvent"));
	}

	[Test, Category("LongRunning")]
	public void only_return_events_that_are_not_system_events() {
		var filter = Filter.ExcludeSystemEvents;
		var foundEvents = new ConcurrentBag<ResolvedEvent>();
		var appeared = new CountdownEvent(20);

		Subscribe(filter, foundEvents, appeared);

		if (!appeared.Wait(Timeout)) {
			Assert.Fail("Appeared countdown event timed out.");
		}

		Assert.True(foundEvents.All(e => !e.Event.EventType.StartsWith("$")));
	}

	private void Subscribe(Filter filter, ConcurrentBag<ResolvedEvent> foundEvents, CountdownEvent appeared) {
		_conn.FilteredSubscribeToAllFrom(
			Position.Start,
			filter,
			CatchUpSubscriptionFilteredSettings.Default,
			(s, e) => {
				foundEvents.Add(e);
				appeared.Signal();
				return Task.CompletedTask;
			},
			(s, p) => Task.CompletedTask, 5,
			s => {
				_conn.AppendToStreamAsync("stream-a", ExpectedVersion.Any, _testEventsAfter.EvenEvents()).Wait();
				_conn.AppendToStreamAsync("stream-b", ExpectedVersion.Any, _testEventsAfter.OddEvents()).Wait();
			});
	}

	[TearDown]
	public override async Task TearDown() {
		_conn.Close();
		await _node.Shutdown();
		await base.TearDown();
	}
}
