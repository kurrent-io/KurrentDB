// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI.Common;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_filter;

[TestFixture]
public class include_everything_handling_deleted_notifications_event_filter : TestFixtureWithEventFilter {
	protected override void Given() {
		_builder.FromAll();
		_builder.AllEvents();
		_builder.SetHandlesStreamDeletedNotifications();
		_builder.SetByStream();
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
	public void does_not_pass_stream_deleted_event() {
		Assert.IsFalse(_ef.Passes(false, "stream", SystemEventTypes.StreamMetadata, isStreamDeletedEvent: true));
	}
}
