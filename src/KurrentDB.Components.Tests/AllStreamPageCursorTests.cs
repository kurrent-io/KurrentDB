// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Components.Shared;
using Xunit;

namespace KurrentDB.Components.Tests;

// Pure unit tests for the $all bidirectional (commit/prepare) paging state machine.
public class AllStreamPageCursorTests {
	static LogPosition P(long c, long p) => new(c, p);

	[Fact]
	public void Starts_at_head_reading_backward() {
		var c = new AllStreamPageCursor();
		Assert.Equal(LogPosition.Head, c.From);
		Assert.False(c.Forward);
	}

	[Fact]
	public void Backward_page_in_the_middle_sets_gating_anchors_and_url() {
		var c = new AllStreamPageCursor();
		// newest=(100,99) oldest=(80,79); read's prev=newer side, next=older side; tail far ahead.
		var outcome = c.OnLoaded(P(100, 99), P(80, 79), prevPos: P(110, 109), nextPos: P(79, 78),
			tfLastCommit: 200, isEndOfStream: false);

		Assert.Equal(AllPageLoad.Applied, outcome);
		Assert.True(c.HasOlderPage);                 // !isEndOfStream
		Assert.True(c.HasNewerPage);                 // newest commit 100 < tail 200
		Assert.Equal(P(100, 99), c.UrlPosition);     // newest shown

		c.ToOlder();
		Assert.Equal(P(79, 78), c.From);             // older anchor = read's NextPos
		Assert.False(c.Forward);

		c.ToNewer();
		Assert.Equal(P(110, 109), c.From);           // newer anchor = read's PrevPos
		Assert.True(c.Forward);
	}

	[Fact]
	public void At_head_backward_has_no_newer_and_reaching_start_has_no_older() {
		var c = new AllStreamPageCursor();
		// newest commit == tail => no newer; isEndOfStream => no older.
		c.OnLoaded(P(200, 199), P(180, 179), prevPos: P(201, 200), nextPos: P(179, 178),
			tfLastCommit: 200, isEndOfStream: true);

		Assert.False(c.HasNewerPage);
		Assert.False(c.HasOlderPage);
	}

	[Fact]
	public void Forward_page_derives_gating_from_newest_and_oldest() {
		var c = new AllStreamPageCursor();
		c.ToNewer();                                 // forward
		Assert.True(c.Forward);

		c.OnLoaded(P(150, 149), P(120, 119), prevPos: P(119, 118), nextPos: P(151, 150),
			tfLastCommit: 200, isEndOfStream: false);

		Assert.True(c.HasNewerPage);                 // newest 150 < tail 200
		Assert.True(c.HasOlderPage);                 // oldest commit 120 > 0
		Assert.Equal(P(150, 149), c.UrlPosition);

		c.ToOlder();
		Assert.Equal(P(119, 118), c.From);           // forward older anchor = PrevPos
		Assert.False(c.Forward);
		c.ToNewer();
		Assert.Equal(P(151, 150), c.From);           // forward newer anchor = NextPos
		Assert.True(c.Forward);
	}

	[Fact]
	public void Forward_oldest_page_has_no_older() {
		var c = new AllStreamPageCursor();
		c.ToOldest();
		Assert.Equal(LogPosition.Start, c.From);
		Assert.True(c.Forward);

		c.OnLoaded(P(30, 29), P(0, -1), prevPos: P(-1, -1), nextPos: P(31, 30),
			tfLastCommit: 200, isEndOfStream: false);

		Assert.False(c.HasOlderPage);                // oldest commit 0 is the start of the log
		Assert.True(c.HasNewerPage);
	}

	[Fact]
	public void Forward_read_past_the_head_falls_back_to_newest() {
		var c = new AllStreamPageCursor();
		c.ToNewer();
		Assert.True(c.Forward);

		var outcome = c.OnLoaded(newest: null, oldest: null, prevPos: P(0, 0), nextPos: P(0, 0),
			tfLastCommit: 200, isEndOfStream: false);

		Assert.Equal(AllPageLoad.ReloadFromNewest, outcome);
		Assert.Equal(LogPosition.Head, c.From);      // reset to the newest page
		Assert.False(c.Forward);
	}

	[Fact]
	public void Empty_backward_page_has_no_newer() {
		var c = new AllStreamPageCursor();
		c.OnLoaded(newest: null, oldest: null, prevPos: P(0, 0), nextPos: P(0, 0),
			tfLastCommit: 200, isEndOfStream: true);

		Assert.False(c.HasNewerPage);
		Assert.False(c.HasOlderPage);                // !isEndOfStream
	}

	[Fact]
	public void Seed_from_url_reads_backward_from_the_anchor() {
		var c = new AllStreamPageCursor();
		c.SeedFrom(P(500, 499));
		Assert.Equal(P(500, 499), c.From);
		Assert.False(c.Forward);
	}
}
