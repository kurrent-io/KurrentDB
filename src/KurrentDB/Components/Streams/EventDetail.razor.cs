// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Cluster;
using KurrentDB.Core.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace KurrentDB.Components.Streams;

public partial class EventDetail : ComponentBase {
	[Inject] StreamsService StreamsService { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string StreamId { get; set; }
	[Parameter] public long EventNumber { get; set; }

	EventRecord _event;          // the record at this position (the link itself, for a link event)
	string _data = "";
	string _metadata = "";

	bool _isLink;                // true when this record is a $> link
	EventRecord _linkedEvent;    // the resolved target (null if the target is gone)
	string _linkedData = "";
	string _linkedMetadata = "";

	string _error;                   // exception message (non read-result errors)
	ReadEventResult? _readError;     // read-result error code, rendered (with a stream link) in markup
	bool _loading = true;
	long _lastEventNumber = -1;  // last event number in the stream; gates the Next button
	ClaimsPrincipal _principal;

	bool HasError => _error != null || _readError != null;
	bool IsAllStream => StreamId == "$all";
	string StreamUrl => $"/ui/streams/{Uri.EscapeDataString(StreamId)}";

	// Editable navigation fields, pre-filled with the current stream/event number.
	string _streamInput = "";
	string _eventNumberInput = "";

	long _firstEventNumber;       // first available event number (a truncated stream may start above 0)

	bool HasNext => _lastEventNumber >= 0 && EventNumber < _lastEventNumber;
	bool HasPrevious => EventNumber > _firstEventNumber;

	string _loadedKey;

	// Next/Previous change the EventNumber on the same route, so the component is reused and
	// OnInitialized won't re-run — load from OnParametersSet, guarded so we only reload on change.
	protected override async Task OnParametersSetAsync() {
		var decoded = Uri.UnescapeDataString(StreamId);
		var key = $"{decoded}/{EventNumber}";
		if (key == _loadedKey)
			return;
		_loadedKey = key;
		StreamId = decoded;
		_streamInput = decoded;
		_eventNumberInput = EventNumber.ToString();
		await Load();
	}

	void Go() {
		if (string.IsNullOrWhiteSpace(_streamInput))
			return;
		if (!long.TryParse(_eventNumberInput?.Trim(), out var num) || num < 0)
			return;
		Navigation.NavigateTo($"/ui/streams/{Uri.EscapeDataString(_streamInput.Trim())}/{num}");
	}

	void OnKeyUp(KeyboardEventArgs e) {
		if (e.Key == "Enter")
			Go();
	}

	// Open the Add Event dialog prefilled from the current event (stream editable), so it can
	// be used as a template for a new event.
	async Task AddSimilar() {
		if (_event is null)
			return;

		// Writes only succeed on the leader; from a follower, send the user to this page on the leader
		// instead of opening a dialog that would fail on submit.
		if (await LeaderRedirect.InterceptNonLeader(Gossip, DialogService, Navigation,
				$"/ui/streams/{Uri.EscapeDataString(StreamId)}/{EventNumber}", "Adding a record"))
			return;

		var parameters = new DialogParameters<AddEventDialog> {
			{ x => x.StreamId, _event.EventStreamId },
			{ x => x.AllowStreamEdit, true },
			{ x => x.EventType, _event.EventType },
			{ x => x.Data, _data },
			{ x => x.Metadata, _metadata },
			{ x => x.Principal, _principal },
			{ x => x.OnEventAdded, EventCallback.Factory.Create(this, Load) }
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true };
		await DialogService.ShowAsync<AddEventDialog>("Add Record", parameters, options);
	}

	async Task Load() {
		_loading = true;
		_error = null;
		_readError = null;
		_event = null;
		_data = "";
		_metadata = "";
		_isLink = false;
		_linkedEvent = null;
		_linkedData = "";
		_linkedMetadata = "";
		_lastEventNumber = -1;
		_firstEventNumber = 0;

		// $all is addressed by log position, not event number — nothing to read by number here.
		if (IsAllStream) {
			_loading = false;
			return;
		}

		_principal = (await AuthenticationState).User;

		try {
			using var cts = new CancellationTokenSource(5000);
			var result = await StreamsService.ReadEventAsync(_principal, StreamId, EventNumber, cts.Token);

			// Find the stream's last event number so we don't let "Next" walk off the end into a 404.
			var tail = await StreamsService.ReadStreamBackwardAsync(_principal, StreamId, -1, 1, cts.Token);
			if (tail.Result == ReadStreamResult.Success)
				_lastEventNumber = tail.LastEventNumber;

			// And the first *available* event number (a maxCount/truncated stream may start above 0)
			// so "Previous" can't step into a truncated gap.
			var head = await StreamsService.ReadStreamForwardAsync(_principal, StreamId, 0, 1, cts.Token);
			if (head.Result == ReadStreamResult.Success && head.Events.Count > 0)
				_firstEventNumber = head.Events[0].OriginalEventNumber;

			if (result.Result == ReadEventResult.Success && result.Record.OriginalEvent != null) {
				var re = result.Record;
				// Show the actual record at this position. For a link event that's the link itself.
				_event = re.OriginalEvent;
				_data = FormatJson(_event.Data);
				if (_event.Metadata.Length > 0)
					_metadata = FormatJson(_event.Metadata);

				// If this is a link, also surface the event it points at.
				_isLink = re.Link != null;
				if (_isLink && re.Event is { } target) {
					_linkedEvent = target;
					_linkedData = FormatJson(target.Data);
					if (target.Metadata.Length > 0)
						_linkedMetadata = FormatJson(target.Metadata);
				}
			} else {
				// A truncated/scavenged event reads back as Success with no record; treat that as
				// not found. The message (with the stream as a link) is composed in markup.
				_readError = result.Result == ReadEventResult.Success ? ReadEventResult.NotFound : result.Result;
			}
		} catch (Exception ex) {
			_error = ex.Message;
		} finally {
			_loading = false;
		}
	}

	static string FormatJson(ReadOnlyMemory<byte> bytes) => EventFormatting.FormatJson(bytes);
}
