// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;

namespace EventStore.Core.Tests.Helpers.IODispatcherTests.QueueWriteEventsTests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_requesting_multiple_writes_with_different_keys<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected override void Given() {
		_ioDispatcher.QueueWriteEvents(Guid.NewGuid(), $"stream-{Guid.NewGuid()}", ExpectedVersion.Any,
			new Event[] { new Event(Guid.NewGuid(), "event-type", false, string.Empty, string.Empty) },
			SystemAccounts.System, (msg) => { });
		_ioDispatcher.QueueWriteEvents(Guid.NewGuid(), $"stream-{Guid.NewGuid()}", ExpectedVersion.Any,
			new Event[] { new Event(Guid.NewGuid(), "event-type", false, string.Empty, string.Empty) },
			SystemAccounts.System, (msg) => { });
	}

	[Test]
	public void should_have_as_many_writes_in_flight_as_unique_keys() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}
}
