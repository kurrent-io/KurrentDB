// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_filter;

[TestFixture]
public class specific_streams_event_filter : TestFixtureWithEventFilter {
	protected override void Given() {
		_builder.FromStream("a");
		_builder.FromStream("b");
		_builder.AllEvents();
	}

	[Test]
	public void can_be_built() {
		Assert.IsNotNull(_ef);
	}

	[Test]
	public void passes_categorized_event_with_correct_stream_id() {
		//NOTE: this is possible if you read from $ce-account stream
		// this is not the same as reading an account category as you can see at 
		// least StreamCreate even there
		Assert.IsTrue(_ef.Passes(true, "a", "event"));
	}

	[Test]
	public void does_not_pass_categorized_event_with_incorrect_stream_id() {
		Assert.IsFalse(_ef.Passes(true, "incorrect_stream", "event"));
	}

	[Test]
	public void passes_uncategorized_event_with_correct_stream_id() {
		Assert.IsTrue(_ef.Passes(false, "b", "event"));
	}

	[Test]
	public void does_not_pass_uncategorized_event_with_incorrect_stream_id() {
		Assert.IsFalse(_ef.Passes(true, "incorrect_stream", "event"));
	}
}
