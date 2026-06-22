// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Components.Shared;

// Cursor for book-order paging of a stream read backwards (newest-first pages), by event number.
// `From` is the anchor passed to a ReadStreamEventsBackward call; -1 means "from the head" (newest).
// The owning component reads a page using From/PageSize, then calls OnLoaded(...) with that page's
// newest/oldest event numbers and whether the read reached the start of the stream. The navigation
// methods then move the anchor. This isolates the (historically bug-prone) anchor/gating maths so it
// can be unit-tested independently of any renderer or read.
public sealed class StreamPageCursor {
	public const long FromHead = -1;

	long? _firstEventNumber;   // newest event number in the page that was last loaded
	long? _lastEventNumber;    // oldest event number in the page that was last loaded

	public StreamPageCursor(int pageSize) => PageSize = Normalize(pageSize);

	public int PageSize { get; private set; }
	public long From { get; private set; } = FromHead;

	// There are older events beyond the current page (the last read did not reach the start).
	public bool HasOlderPage { get; private set; }
	// The current page is not the newest page (we paged away from the head).
	public bool HasNewerPage { get; private set; }

	// Record the outcome of the read that used the current From. firstEventNumber/lastEventNumber are
	// the newest/oldest event numbers in the returned page (null for an empty page).
	public void OnLoaded(long? firstEventNumber, long? lastEventNumber, bool isEndOfStream) {
		HasOlderPage = !isEndOfStream;
		HasNewerPage = From != FromHead;
		_firstEventNumber = firstEventNumber;
		_lastEventNumber = lastEventNumber;
	}

	public void ToNewest() => From = FromHead;

	public void ToOldest() => From = PageSize - 1;

	public void ToOlder() {
		if (_lastEventNumber is { } last)
			From = last - 1;
	}

	public void ToNewer() {
		if (_firstEventNumber is { } first)
			From = first + PageSize;
	}

	public void ChangePageSize(int pageSize) {
		PageSize = Normalize(pageSize);
		From = FromHead;   // reset to the newest page so paging stays consistent with the new size
	}

	static int Normalize(int pageSize) => int.Max(1, pageSize);
}
