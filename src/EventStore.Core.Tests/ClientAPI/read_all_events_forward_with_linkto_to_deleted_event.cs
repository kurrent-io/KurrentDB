// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class read_all_events_forward_with_linkto_to_deleted_event<TLogFormat, TStreamId> : SpecificationWithLinkToToDeletedEvents<TLogFormat, TStreamId> {
	private StreamEventsSlice _read;

	protected override async Task When() {
		_read = await _conn.ReadStreamEventsForwardAsync(LinkedStreamName, 0, 1, true, null);
	}

	[Test]
	public void one_event_is_read() {
		Assert.AreEqual(1, _read.Events.Length);
	}

	[Test]
	public void the_linked_event_is_not_resolved() {
		Assert.IsNull(_read.Events[0].Event);
	}

	[Test]
	public void the_link_event_is_included() {
		Assert.IsNotNull(_read.Events[0].OriginalEvent);
	}

	[Test]
	public void the_event_is_not_resolved() {
		Assert.IsFalse(_read.Events[0].IsResolved);
	}
}
