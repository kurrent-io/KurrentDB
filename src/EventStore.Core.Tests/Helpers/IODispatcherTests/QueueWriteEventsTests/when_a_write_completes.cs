// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;

namespace EventStore.Core.Tests.Helpers.IODispatcherTests.QueueWriteEventsTests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_write_completes<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private bool _completed = false;

	protected override void Given() {
		AllWritesQueueUp();

		_ioDispatcher.QueueWriteEvents(Guid.NewGuid(), $"stream-{Guid.NewGuid()}", ExpectedVersion.Any,
			new Event[] { new Event(Guid.NewGuid(), "event-type", false, string.Empty, string.Empty) },
			SystemAccounts.System, (msg) => { _completed = true; });
		OneWriteCompletes();
	}

	[Test]
	public void should_invoke_callback_when_write_completes() {
		Assert.IsTrue(_completed);
	}
}
