// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;
using System;
using System.Linq;

namespace EventStore.Core.Tests.Helpers.IODispatcherTests.QueueWriteEventsTests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_requesting_multiple_writes_with_the_same_key<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected override void Given() {
		AllWritesQueueUp();

		var key = Guid.NewGuid();
		_ioDispatcher.QueueWriteEvents(key, $"stream-{Guid.NewGuid()}", ExpectedVersion.Any,
			new Event[] {new Event(Guid.NewGuid(), "event-type", false, string.Empty, string.Empty)},
			SystemAccounts.System, (msg) => { });
		_ioDispatcher.QueueWriteEvents(key, $"stream-{Guid.NewGuid()}", ExpectedVersion.Any,
			new Event[] {new Event(Guid.NewGuid(), "event-type", false, string.Empty, string.Empty)},
			SystemAccounts.System, (msg) => { });
		_ioDispatcher.QueueWriteEvents(key, $"stream-{Guid.NewGuid()}", ExpectedVersion.Any,
			new Event[] {new Event(Guid.NewGuid(), "event-type", false, string.Empty, string.Empty)},
			SystemAccounts.System, (msg) => { });
	}

	[Test]
	public void should_only_have_a_single_write_in_flight() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void should_continue_to_only_have_a_single_write_in_flight_as_writes_complete() {
		var writeRequests = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>();

		//first write
		_consumer.HandledMessages.Clear();
		OneWriteCompletes();
		Assert.AreEqual(1, writeRequests.Count());

		//second write
		_consumer.HandledMessages.Clear();
		OneWriteCompletes();
		Assert.AreEqual(1, writeRequests.Count());

		//third write completes, no more writes left in the queue
		_consumer.HandledMessages.Clear();
		OneWriteCompletes();
		Assert.AreEqual(0, writeRequests.Count());
	}
}
