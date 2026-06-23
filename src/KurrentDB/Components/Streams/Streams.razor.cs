// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace KurrentDB.Components.Streams;

public partial class Streams : ComponentBase {
	[Inject] StreamsService StreamsService { get; set; } = null!;
	[Inject] IAuthorizationProvider Authorizer { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;
	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	string[] _recentlyChanged = [];
	string[] _recentlyCreated = [];
	string _searchStream = "";
	ClaimsPrincipal _principal;
	bool _canBrowseAll;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		// Enable "Browse $all" only if the user is actually allowed Streams.Read on $all (the same op the
		// service enforces), rather than assuming admin — so a non-admin granted $all read sees it enabled.
		using (var cts = new CancellationTokenSource(5000)) {
			_canBrowseAll = await Authorizer.CheckAccessAsync(_principal, UiOperations.ReadAll, cts.Token);
		}
		await LoadStreams();
	}

	void BrowseAll() => Navigation.NavigateTo($"/ui/streams/{Uri.EscapeDataString("$all")}");

	bool _showSystemStreams;

	async Task LoadStreams() {
		try {
			using var cts = new CancellationTokenSource(5000);
			// Recently created: $streams contains links to stream metadata events.
			// The resolved Event.EventStreamId gives us the stream name.
			var created = await StreamsService.ReadStreamBackwardAsync(_principal, "$streams", -1, 50, cts.Token);
			if (created.Result == ReadStreamResult.Success) {
				_recentlyCreated = created.Events
					.Where(e => e.Event != null)
					.Select(e => e.Event.EventStreamId)
					.Where(s => !string.IsNullOrEmpty(s) && (_showSystemStreams || !s.StartsWith("$")))
					.Distinct()
					.ToArray();
			}
		} catch (Exception) {
			// Ignore
		}

		try {
			using var cts = new CancellationTokenSource(5000);
			// Recently changed: read $all backward, extract unique stream names.
			var changed = await StreamsService.ReadAllBackwardAsync(_principal, -1, -1, 100, cts.Token);
			if (changed.Result == ReadAllResult.Success) {
				_recentlyChanged = changed.Events
					.Where(e => e.Event != null)
					.Select(e => e.Event.EventStreamId)
					.Where(s => !string.IsNullOrEmpty(s) && (_showSystemStreams || !s.StartsWith("$")))
					.Distinct()
					.ToArray();
			}
		} catch (Exception) {
			// Ignore
		}
	}

	async Task OnShowSystemStreamsChanged(bool value) {
		_showSystemStreams = value;
		await LoadStreams();
	}

	async Task AddEvent() {
		// Writes only succeed on the leader; from a follower, send the user to the stream browser on the
		// leader instead of opening a dialog that would fail on submit.
		if (await LeaderRedirect.InterceptNonLeader(Gossip, DialogService, Navigation,
				"/ui/streams", "Adding a record"))
			return;

		// The dialog writes the event(s) itself and stays open for multiple adds, refreshing the
		// recently-changed/created lists after each via OnEventAdded.
		var parameters = new DialogParameters<AddEventDialog> {
			{ x => x.Principal, _principal },
			{ x => x.OnEventAdded, EventCallback.Factory.Create(this, LoadStreams) }
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true };
		await DialogService.ShowAsync<AddEventDialog>("Add Record", parameters, options);
	}

	void GoToStream() {
		if (!string.IsNullOrWhiteSpace(_searchStream))
			Navigation.NavigateTo($"/ui/streams/{Uri.EscapeDataString(_searchStream.Trim())}");
	}

	void OnSearchKeyUp(KeyboardEventArgs e) {
		if (e.Key == "Enter")
			GoToStream();
	}
}
