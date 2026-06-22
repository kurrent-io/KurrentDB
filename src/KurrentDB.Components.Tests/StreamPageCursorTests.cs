// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Components.Shared;
using Xunit;

namespace KurrentDB.Components.Tests;

// Pure unit tests for the backward-paging anchor/gating state machine.
public class StreamPageCursorTests {
	[Fact]
	public void Starts_at_head() {
		var c = new StreamPageCursor(20);
		Assert.Equal(StreamPageCursor.FromHead, c.From);
		Assert.Equal(20, c.PageSize);
	}

	[Fact]
	public void First_full_page_has_older_but_no_newer() {
		var c = new StreamPageCursor(20);
		c.OnLoaded(firstEventNumber: 99, lastEventNumber: 80, isEndOfStream: false);
		Assert.True(c.HasOlderPage);     // read did not reach the start
		Assert.False(c.HasNewerPage);    // we're at the head
	}

	[Fact]
	public void Older_anchors_just_before_the_oldest_shown() {
		var c = new StreamPageCursor(20);
		c.OnLoaded(99, 80, isEndOfStream: false);
		c.ToOlder();
		Assert.Equal(79, c.From);        // last - 1
	}

	[Fact]
	public void Reaching_the_start_clears_older_and_sets_newer() {
		var c = new StreamPageCursor(20);
		c.OnLoaded(99, 80, false);
		c.ToOlder();
		c.OnLoaded(79, 60, isEndOfStream: true);
		Assert.False(c.HasOlderPage);    // reached event 0 / start
		Assert.True(c.HasNewerPage);     // paged away from head
	}

	[Fact]
	public void Newer_anchors_one_page_above_the_newest_shown() {
		var c = new StreamPageCursor(20);
		c.OnLoaded(99, 80, false);
		c.ToOlder();
		c.OnLoaded(79, 60, false);
		c.ToNewer();
		Assert.Equal(99, c.From);        // first(79) + pageSize(20)
	}

	[Fact]
	public void Newest_returns_to_head() {
		var c = new StreamPageCursor(20);
		c.OnLoaded(99, 80, false);
		c.ToOlder();
		c.ToNewest();
		Assert.Equal(StreamPageCursor.FromHead, c.From);
	}

	[Fact]
	public void Oldest_anchors_at_page_size_minus_one() {
		var c = new StreamPageCursor(20);
		c.ToOldest();
		Assert.Equal(19, c.From);
	}

	[Fact]
	public void Navigation_is_a_noop_on_an_empty_page() {
		var c = new StreamPageCursor(20);
		c.ToOldest();
		c.OnLoaded(firstEventNumber: null, lastEventNumber: null, isEndOfStream: true);
		var before = c.From;
		c.ToOlder();
		c.ToNewer();
		Assert.Equal(before, c.From);    // nothing to anchor against
	}

	[Fact]
	public void Changing_page_size_resets_to_head_and_clamps() {
		var c = new StreamPageCursor(20);
		c.OnLoaded(99, 80, false);
		c.ToOlder();
		c.ChangePageSize(50);
		Assert.Equal(50, c.PageSize);
		Assert.Equal(StreamPageCursor.FromHead, c.From);

		c.ChangePageSize(0);
		Assert.Equal(1, c.PageSize);     // clamped to a minimum of 1
	}
}
