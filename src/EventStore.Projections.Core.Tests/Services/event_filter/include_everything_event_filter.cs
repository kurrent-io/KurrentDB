// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.ClientAPI.Common;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_filter;

[TestFixture]
public class include_everything_event_filter : TestFixtureWithEventFilter {
	protected override void Given() {
		_builder.FromAll();
		_builder.AllEvents();
	}

	[Test]
	public void can_be_built() {
		Assert.IsNotNull(_ef);
	}

	[Test]
	public void does_not_pass_categorized_event() {
		Assert.IsFalse(_ef.Passes(true, "$ce-stream", "event"));
	}

	[Test]
	public void passes_uncategorized_event() {
		Assert.IsTrue(_ef.Passes(false, "stream", "event"));
	}

	[Test]
	public void passes_stream_deleted_event() {
		Assert.IsTrue(_ef.Passes(false, "stream", SystemEventTypes.StreamMetadata, isStreamDeletedEvent: true));
	}
}
