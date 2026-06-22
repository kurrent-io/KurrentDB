// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace KurrentDB.Components.Streams;

public sealed partial class StreamDetail : ComponentBase, IDisposable {
	[Inject] StreamsService StreamsService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string StreamId { get; set; }

	// Persisted in the URL so a refresh keeps the current view options.
	[SupplyParameterFromQuery(Name = "live")] public bool? LiveQuery { get; set; }
	[SupplyParameterFromQuery(Name = "resolve")] public bool? ResolveQuery { get; set; }
	[SupplyParameterFromQuery(Name = "pageSize")] public int? PageSizeQuery { get; set; }
	// Page anchor: for a stream the from-event-number, for $all a "commit-prepare" position. Absent = head.
	[SupplyParameterFromQuery(Name = "pos")] public string PosQuery { get; set; }

	int _pageSize = 20;

	IReadOnlyList<ResolvedEvent> _events = [];
	bool _loading = true;
	string _error;
	bool _hasNext;
	bool _hasPrevious;
	bool _live = true;
	bool _resolveLinks;
	int _newEventsCount;
	string _searchStream = "";
	CancellationTokenSource _liveCts;
	ClaimsPrincipal _principal;

	bool IsAllStream => StreamId == "$all";

	// The target a link row points at, for the separate "Links to" column. The main columns
	// always show the record at this position (the link itself via OriginalEvent), unchanged.
	// Number is -1 and Type/Stream may be null when the target can't be determined.
	readonly record struct LinkTarget(string Stream, long Number, string Type, bool Deleted);

	// Returns null for a plain (non-link) row. For a resolved link, returns the target; if the
	// target has been deleted/scavenged, returns what the link pointed at, flagged as deleted.
	static LinkTarget? GetLinkTarget(ResolvedEvent re) {
		if (re.Link == null)
			return null; // not a resolved link (plain event, or resolution is off)

		if (re.Event is { } target)
			return new LinkTarget(target.EventStreamId, target.EventNumber, target.EventType, false);

		var (num, stream) = ParsePointer(re.Link);
		return new LinkTarget(stream, num ?? -1, null, true);
	}

	// A $> link's data is the UTF-8 string "<eventNumber>@<streamName>".
	static (long? Number, string Stream) ParsePointer(EventRecord link) {
		try {
			var s = System.Text.Encoding.UTF8.GetString(link.Data.Span);
			var at = s.IndexOf('@');
			if (at > 0 && long.TryParse(s[..at], out var n))
				return (n, s[(at + 1)..]);
		} catch {
			// fall through
		}
		return (null, null);
	}

	// Regular-stream paging: a from-event-number cursor (kept inline — uses a data-derived "newer exists"
	// check that's more precise than the shared StreamPageCursor's "paged away from head").
	long _currentFrom = -1;

	// $all paging is bidirectional by log position — delegated to the tested AllStreamPageCursor.
	AllStreamPageCursor _allCursor = new();

	string _loadedStreamId;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
	}

	protected override async Task OnParametersSetAsync() {
		var decoded = Uri.UnescapeDataString(StreamId);
		if (decoded == _loadedStreamId)
			return; // same stream; nothing to reload (e.g. unrelated re-render)
		_loadedStreamId = decoded;
		StreamId = decoded;

		// Reset all per-stream state so navigating between streams (e.g. via "Go to stream"
		// or a link from $all) starts fresh — the route is reused, so OnInitialized won't re-run.
		StopLiveSubscription();
		_events = [];
		_error = null;
		// Pre-fill the navigation field with the current stream; editing + Enter navigates elsewhere.
		_searchStream = decoded;
		_currentFrom = -1;
		_allCursor = new AllStreamPageCursor();
		_newEventsCount = 0;
		_hasNext = false;
		_hasPrevious = false;
		// Restore view options from the URL, falling back to defaults. Resolve defaults on for
		// normal/category streams (you want the targets) but off for $all (resolving just duplicates).
		_live = LiveQuery ?? true;
		_resolveLinks = ResolveQuery ?? !IsAllStream;
		_pageSize = PageSizeQuery is > 0 ? PageSizeQuery.Value : 20;
		SeedPositionFromQuery();

		await LoadEvents();
		StartLiveSubscription();
	}

	// Seed the read cursor from the ?pos= anchor (absent = head). Stream: "<eventNumber>".
	// $all: "<commit>-<prepare>". Invalid/absent leaves the cursor at the newest page (-1).
	void SeedPositionFromQuery() {
		if (string.IsNullOrWhiteSpace(PosQuery))
			return;

		if (IsAllStream) {
			var parts = PosQuery.Split('-');
			if (parts.Length == 2 && long.TryParse(parts[0], out var c) && long.TryParse(parts[1], out var p))
				_allCursor.SeedFrom(new LogPosition(c, p)); // backward read from the URL anchor reproduces the page
		} else if (long.TryParse(PosQuery, out var n)) {
			_currentFrom = n;
		}
	}

	// The current page's anchor for the URL, or null at the head (so the param is dropped).
	string CurrentPos() {
		if (!_hasPrevious)
			return null;
		return IsAllStream
			? $"{_allCursor.UrlPosition.Commit}-{_allCursor.UrlPosition.Prepare}"
			: _currentFrom.ToString();
	}

	// Reflect the current view options into the URL (replace, so it doesn't spam history)
	// so they survive a refresh.
	void UpdateUrlState() {
		var uri = Navigation.GetUriWithQueryParameters(new Dictionary<string, object> {
			["live"] = _live,
			["resolve"] = _resolveLinks,
			["pageSize"] = _pageSize,
			["pos"] = CurrentPos(), // null at head -> removed from the URL
		});
		Navigation.NavigateTo(uri, replace: true);
	}

	void OnLiveChanged(bool value) {
		_live = value;
		UpdateUrlState();
	}

	void GoToStream() {
		if (!string.IsNullOrWhiteSpace(_searchStream))
			Navigation.NavigateTo($"/ui/streams/{Uri.EscapeDataString(_searchStream.Trim())}");
	}

	void OnSearchKeyUp(KeyboardEventArgs e) {
		if (e.Key == "Enter")
			GoToStream();
	}

	void StartLiveSubscription() {
		StopLiveSubscription();
		// Only subscribe on the interactive instance — not during server prerender, which would
		// otherwise create a second, redundant subscription delivering duplicate live events.
		if (!RendererInfo.IsInteractive)
			return;
		var cts = new CancellationTokenSource();
		_liveCts = cts;
		// Subscribe for live events appearing after this point (the service auto-reconnects on a drop). The
		// handler only prepends while we're viewing the head (and Live is on); paging into history pauses it.
		_ = RunLiveSubscription(cts);
	}

	async Task RunLiveSubscription(CancellationTokenSource cts) {
		try {
			await StreamsService.SubscribeAsync(_principal, StreamId, OnLiveEvent, _resolveLinks, cts.Token);
		} catch (StreamsException) {
			// Not permitted to subscribe — live updates are best-effort; the already-authorized page read stands.
		} catch (OperationCanceledException) {
			// Subscription stopped (navigated away, options changed, or torn down) — expected.
		} finally {
			// Owned here so it's disposed only once the loop has fully unwound (StopLiveSubscription just cancels).
			cts.Dispose();
		}
	}

	void StopLiveSubscription() {
		if (_liveCts is { } cts) {
			_liveCts = null;
			cts.Cancel(); // RunLiveSubscription disposes it once the loop unwinds
		}
	}

	async Task OnResolveLinksChanged(bool value) {
		_resolveLinks = value;
		await LoadEvents();
		// Re-subscribe so live rows resolve (or not) the same way as paged rows.
		StartLiveSubscription();
		UpdateUrlState();
	}

	void OnLiveEvent(ResolvedEvent evt) {
		try {
			_ = InvokeAsync(() => ApplyLiveEvent(evt));
		} catch (ObjectDisposedException) {
			// Subscription delivered an event after teardown — ignore.
		}
	}

	void ApplyLiveEvent(ResolvedEvent evt) {
		// Skip events that aren't newer than what's already shown — e.g. one we just added
		// ourselves and reloaded into view. (The prepend path below has always needed this.)
		if (_events.Count > 0 && !IsNewer(evt, _events[0]))
			return;

		// Auto-append only when viewing the head with Live on. Otherwise (in history,
		// or Live paused) just count it and surface a "N new events" pill to jump back.
		if (_hasPrevious || !_live) {
			_newEventsCount++;
			StateHasChanged();
			return;
		}

		var list = new List<ResolvedEvent>(_events.Count + 1) { evt };
		list.AddRange(_events);
		if (list.Count > _pageSize)
			list.RemoveRange(_pageSize, list.Count - _pageSize);
		_events = list;
		StateHasChanged();
	}

	// True if 'candidate' is strictly newer than the current top-of-page event.
	bool IsNewer(ResolvedEvent candidate, ResolvedEvent top) {
		if (IsAllStream) {
			var c = candidate.OriginalPosition;
			var t = top.OriginalPosition;
			if (!c.HasValue || !t.HasValue)
				return true;
			return c.Value.CommitPosition > t.Value.CommitPosition
				|| (c.Value.CommitPosition == t.Value.CommitPosition && c.Value.PreparePosition > t.Value.PreparePosition);
		}
		return candidate.OriginalEventNumber > top.OriginalEventNumber;
	}

	async Task LoadEvents() {
		_loading = true;
		_error = null;
		try {
			using var cts = new CancellationTokenSource(5000);
			if (IsAllStream)
				await LoadAllStreamEvents(cts.Token);
			else
				await LoadStreamEvents(cts.Token);
		} catch (Exception ex) {
			_error = ex.Message;
		} finally {
			_loading = false;
		}

		// Back at the head: any pending "new events" are now shown, so clear the pill.
		if (!_hasPrevious)
			_newEventsCount = 0;
	}

	async Task LoadStreamEvents(CancellationToken ct) {
		var result = await StreamsService.ReadStreamBackwardAsync(
			_principal, StreamId, _currentFrom, _pageSize, ct, resolveLinkTos: _resolveLinks);

		switch (result.Result) {
			case ReadStreamResult.Success:
				_events = result.Events;
				_hasNext = !result.IsEndOfStream;
				// Newer events exist iff the top of this page is older than the stream's last event.
				// (Derived from the data, not the read cursor, so a reload at the head stays "at head".)
				_hasPrevious = _events.Count > 0 && _events[0].OriginalEventNumber < result.LastEventNumber;
				if (_events.Count > 0)
					_currentFrom = result.Events[0].OriginalEventNumber;
				break;
			case ReadStreamResult.NoStream:
				_error = $"Stream '{StreamId}' does not exist.";
				break;
			case ReadStreamResult.StreamDeleted:
				_error = $"Stream '{StreamId}' has been deleted.";
				break;
			case ReadStreamResult.AccessDenied:
				_error = "Access denied.";
				break;
			default:
				_error = $"Error reading stream: {result.Result}";
				break;
		}
	}

	async Task LoadAllStreamEvents(CancellationToken ct) {
		if (_allCursor.Forward) {
			// Forward read (paging towards newer / oldest page). Returns oldest-first; reverse to newest-first.
			var result = await StreamsService.ReadAllForwardAsync(
				_principal, _allCursor.From.Commit, _allCursor.From.Prepare, _pageSize, ct, resolveLinkTos: _resolveLinks);

			if (result.Result != ReadAllResult.Success) {
				_error = result.Result == ReadAllResult.AccessDenied ? "Access denied." : $"Error reading $all: {result.Result}";
				return;
			}

			// oldest-first
			var events = result.Events;
			// Forward read: oldest is first, newest is last.
			var newest = events.Count > 0 ? ToPos(events[^1].OriginalPosition) : null;
			var oldest = events.Count > 0 ? ToPos(events[0].OriginalPosition) : null;
			if (_allCursor.OnLoaded(newest, oldest, ToPos(result.PrevPos), ToPos(result.NextPos),
				result.TfLastCommitPosition, result.IsEndOfStream) == AllPageLoad.ReloadFromNewest) {
				// Paged past the head (no newer events) — the cursor reset to newest; re-read.
				await LoadAllStreamEvents(ct);
				return;
			}

			var list = new List<ResolvedEvent>(events);
			list.Reverse();
			_events = list;
		} else {
			var result = await StreamsService.ReadAllBackwardAsync(
				_principal, _allCursor.From.Commit, _allCursor.From.Prepare, _pageSize, ct, resolveLinkTos: _resolveLinks);

			if (result.Result != ReadAllResult.Success) {
				_error = result.Result == ReadAllResult.AccessDenied ? "Access denied." : $"Error reading $all: {result.Result}";
				return;
			}

			_events = result.Events; // newest-first
			var newest = _events.Count > 0 ? ToPos(_events[0].OriginalPosition) : null;
			var oldest = _events.Count > 0 ? ToPos(_events[^1].OriginalPosition) : null;
			_allCursor.OnLoaded(newest, oldest, ToPos(result.PrevPos), ToPos(result.NextPos),
				result.TfLastCommitPosition, result.IsEndOfStream);
		}

		_hasNext = _allCursor.HasOlderPage;
		_hasPrevious = _allCursor.HasNewerPage;
	}

	static LogPosition ToPos(TFPos pos) => new(pos.CommitPosition, pos.PreparePosition);
	static LogPosition? ToPos(TFPos? pos) => pos is { } p ? new LogPosition(p.CommitPosition, p.PreparePosition) : null;

	async Task NewestPage() {
		if (IsAllStream)
			_allCursor.ToNewest();
		else
			_currentFrom = -1;
		await LoadEvents();
		UpdateUrlState();
	}

	async Task OldestPage() {
		if (IsAllStream)
			_allCursor.ToOldest();
		else
			_currentFrom = _pageSize - 1;
		await LoadEvents();
		UpdateUrlState();
	}

	// Older = back in time. Stream: read backward from one below the current oldest.
	// $all: read backward from the server's "older" anchor.
	async Task NextPage() {
		if (_events.Count == 0)
			return;
		if (IsAllStream)
			_allCursor.ToOlder();
		else
			_currentFrom = _events[^1].OriginalEventNumber - 1;
		await LoadEvents();
		UpdateUrlState();
	}

	// Newer = forward in time. Stream: read backward from above the current newest.
	// $all: read FORWARD from the server's "newer" anchor (then reversed for display).
	async Task PreviousPage() {
		if (IsAllStream)
			_allCursor.ToNewer();
		else if (_events.Count > 0)
			_currentFrom = _events[0].OriginalEventNumber + _pageSize;
		await LoadEvents();
		UpdateUrlState();
	}

	public void Dispose() {
		StopLiveSubscription();
	}

	async Task OnPageSizeChanged(int value) {
		_pageSize = value < 1 ? 1 : value;
		// Reset to the newest page so paging stays consistent with the new size.
		if (IsAllStream)
			_allCursor.ToNewest();
		else
			_currentFrom = -1;
		await LoadEvents();
		UpdateUrlState();
	}

	async Task AddEvent() {
		// Writes only succeed on the leader; from a follower, send the user to this stream on the leader
		// instead of opening a dialog that would fail on submit.
		if (await LeaderRedirect.InterceptNonLeader(Gossip, DialogService, Navigation,
				$"/ui/streams/{Uri.EscapeDataString(StreamId)}", "Adding a record"))
			return;

		// The dialog performs the write itself and stays open for multiple adds, refreshing us
		// after each via OnEventAdded.
		var parameters = new DialogParameters<AddEventDialog> {
			{ x => x.StreamId, StreamId },
			{ x => x.Principal, _principal },
			{ x => x.OnEventAdded, EventCallback.Factory.Create(this, ReloadNewest) }
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true };
		await DialogService.ShowAsync<AddEventDialog>("Add Record", parameters, options);
	}

	async Task ReloadNewest() {
		if (IsAllStream)
			_allCursor.ToNewest();
		else
			_currentFrom = -1;
		await LoadEvents();
		// Note: no UpdateUrlState() here — it navigates, which would dismiss the Add Event dialog
		// (this runs as that dialog's OnEventAdded callback while it stays open for more adds).
	}

	async Task DeleteStream() {
		var confirmed = await DialogService.ShowMessageBox(
			"Delete Stream",
			$"Are you sure you want to delete stream '{StreamId}'?",
			yesText: "Delete", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(5000);
				await StreamsService.DeleteStreamAsync(_principal, StreamId, cts.Token);
				Snackbar.Add($"Stream '{StreamId}' deleted.", Severity.Success);
				Navigation.NavigateTo("/ui/streams");
			} catch (StreamsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}
}
