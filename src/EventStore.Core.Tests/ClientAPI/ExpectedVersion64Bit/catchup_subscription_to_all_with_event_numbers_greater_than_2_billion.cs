// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using NUnit.Framework;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;
using ResolvedEvent = EventStore.ClientAPI.ResolvedEvent;
using StreamMetadata = EventStore.ClientAPI.StreamMetadata;

namespace EventStore.Core.Tests.ClientAPI.ExpectedVersion64Bit;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
[Category("ClientAPI"), Category("LongRunning")]
public class catchup_subscription_to_all_with_event_numbers_greater_than_2_billion<TLogFormat, TStreamId>
	: MiniNodeWithExistingRecords<TLogFormat, TStreamId> {
	private const long intMaxValue = (long)int.MaxValue;

	private string _streamId = "subscriptions-catchup-all";

	private EventRecord _r1, _r2;

	public override async ValueTask WriteTestScenario(CancellationToken token) {
		_r1 = await WriteSingleEvent(_streamId, intMaxValue + 1, new string('.', 3000), token: token);
		_r2 = await WriteSingleEvent(_streamId, intMaxValue + 2, new string('.', 3000), token: token);
	}

	public override async Task Given() {
		_store = BuildConnection(Node);
		await _store.ConnectAsync();
		await _store.SetStreamMetadataAsync(_streamId, ExpectedVersion.Any,
			StreamMetadata.Create(truncateBefore: intMaxValue + 1));
	}

	[Test]
	public async Task should_be_able_to_subscribe_to_all_with_catchup_subscription() {
		var evnt = new EventData(Guid.NewGuid(), "EventType", false, new byte[10], new byte[15]);
		List<ResolvedEvent> receivedEvents = new List<ResolvedEvent>();

		var countdown = new CountdownEvent(3);

		_store.SubscribeToAllFrom(Position.Start, CatchUpSubscriptionSettings.Default, (s, e) => {
			if (e.Event.EventStreamId == _streamId) {
				receivedEvents.Add(e);
				countdown.Signal();
			}

			return Task.CompletedTask;
		}, userCredentials: DefaultData.AdminCredentials);

		await _store.AppendToStreamAsync(_streamId, intMaxValue + 2, evnt);

		Assert.That(countdown.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for events to appear");

		Assert.AreEqual(_r1.EventId, receivedEvents[0].Event.EventId);
		Assert.AreEqual(_r2.EventId, receivedEvents[1].Event.EventId);
		Assert.AreEqual(evnt.EventId, receivedEvents[2].Event.EventId);
	}
}
