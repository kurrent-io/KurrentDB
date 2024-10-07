// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_filter;

[TestFixture]
public class specific_event_event_filter : TestFixtureWithEventFilter {
	protected override void Given() {
		_builder.FromAll();
		_builder.IncludeEvent("event");
	}

	[Test]
	public void can_be_built() {
		Assert.IsNotNull(_ef);
	}

	[Test]
	public void does_not_pass_categorized_event_with_correct_event_name() {
		Assert.IsFalse(_ef.Passes(true, "stream", "event"));
	}

	[Test]
	public void does_not_pass_categorized_event_with_incorrect_event_name() {
		Assert.IsFalse(_ef.Passes(true, "stream", "incorrect_event"));
	}

	[Test]
	public void passes_uncategorized_event_with_correct_event_name() {
		Assert.IsTrue(_ef.Passes(false, "stream", "event"));
	}

	[Test]
	public void does_not_pass_uncategorized_event_with_incorrect_event_name() {
		Assert.IsFalse(_ef.Passes(true, "stream", "incorrect_event"));
	}
}
