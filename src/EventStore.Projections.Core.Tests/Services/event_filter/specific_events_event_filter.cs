// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_filter;

[TestFixture]
public class specific_events_event_filter : TestFixtureWithEventFilter {
	protected override void Given() {
		_builder.FromAll();
		_builder.IncludeEvent("eventOne");
		_builder.IncludeEvent("eventTwo");
	}

	[Test]
	public void can_be_built() {
		Assert.IsNotNull(_ef);
	}

	[Test]
	public void should_allow_non_linked_events() {
		Assert.IsTrue(_ef.Passes(false, "stream", "eventOne"));
	}

	[Test]
	public void should_allow_events_from_event_type_stream() {
		Assert.IsTrue(_ef.Passes(true, "$et-eventOne", "eventOne"));
	}

	[Test]
	public void should_not_allow_events_from_event_type_stream_that_is_not_included() {
		Assert.IsFalse(_ef.Passes(true, "$et-eventThree", "eventThree"));
	}

	[Test]
	public void should_not_allow_events_from_system_streams() {
		Assert.IsFalse(_ef.Passes(false, "$ct-test", "eventOne"));
	}

	[Test]
	public void should_not_allow_linked_events_from_system_streams() {
		Assert.IsFalse(_ef.Passes(true, "$ct-test", "eventOne"));
	}
}
