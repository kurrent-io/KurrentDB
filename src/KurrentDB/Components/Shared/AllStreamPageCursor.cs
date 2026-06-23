// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Components.Shared;

// A commit/prepare position in the transaction log. Head (-1,-1) = newest; Start (0,0) = beginning.
public readonly record struct LogPosition(long Commit, long Prepare) {
	public static readonly LogPosition Head = new(-1, -1);
	public static readonly LogPosition Start = new(0, 0);
}

public enum AllPageLoad {
	Applied,            // the page was applied; HasOlderPage/HasNewerPage/UrlPosition are updated
	ReloadFromNewest,   // a forward read ran past the head (no newer events) — re-read from the newest page
}

// Cursor for book-order paging of the $all stream, which (unlike a regular stream) is addressed by
// commit/prepare log position and pages bidirectionally: older pages are read backward, newer pages
// forward (then reversed for newest-first display). This isolates the (historically bug-prone) anchor +
// gating maths from the component so it can be unit-tested. The owning component does the actual reads,
// extracts the newest/oldest event positions and the read's Prev/Next positions, and calls OnLoaded.
public sealed class AllStreamPageCursor {
	LogPosition _older = LogPosition.Head;   // anchor for the next older page (read backward)
	LogPosition _newer = LogPosition.Head;   // anchor for the next newer page (read forward)

	// Page size lives on the owning component (it's UI-bound and shared with regular-stream mode, and
	// passed to the reads): $all anchors come from the server's Prev/Next positions, never from page size.

	// The next read: anchor position + direction (false = backward/newer side, true = forward/older side).
	public LogPosition From { get; private set; } = LogPosition.Head;
	public bool Forward { get; private set; }

	public bool HasOlderPage { get; private set; }
	public bool HasNewerPage { get; private set; }

	// The newest event shown — the URL anchor (a backward read from here reproduces the page).
	public LogPosition UrlPosition { get; private set; } = LogPosition.Head;

	public void ToNewest() { From = LogPosition.Head; Forward = false; }
	public void ToOldest() { From = LogPosition.Start; Forward = true; }
	public void ToOlder() { From = _older; Forward = false; }
	public void ToNewer() { From = _newer; Forward = true; }

	// Restore from a URL anchor: a backward read from that position reproduces the page.
	public void SeedFrom(LogPosition anchor) { From = anchor; Forward = false; }

	// Record the outcome of the read issued at the current From/Forward. newest/oldest are the positions of
	// the newest/oldest events in the (display-order) page, null when the page is empty. prevPos/nextPos and
	// isEndOfStream come straight from the read; tfLastCommit is the log's last commit position.
	public AllPageLoad OnLoaded(
		LogPosition? newest, LogPosition? oldest, LogPosition prevPos, LogPosition nextPos,
		long tfLastCommit, bool isEndOfStream) {

		if (Forward) {
			if (newest is null) {
				// Paged past the head — there are no newer events. Fall back to the newest page.
				ToNewest();
				return AllPageLoad.ReloadFromNewest;
			}
			_older = prevPos;
			_newer = nextPos;
			HasNewerPage = newest.Value.Commit < tfLastCommit;   // newest shown isn't the tail of the log
			HasOlderPage = oldest!.Value.Commit > 0;             // oldest shown isn't the start of the log
			UrlPosition = newest.Value;
		} else {
			_older = nextPos;
			_newer = prevPos;
			HasOlderPage = !isEndOfStream;
			HasNewerPage = newest is { } n && n.Commit < tfLastCommit;
			if (newest is { } shown)
				UrlPosition = shown;
		}
		return AllPageLoad.Applied;
	}
}
