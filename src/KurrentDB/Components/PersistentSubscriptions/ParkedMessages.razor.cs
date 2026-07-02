// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.PersistentSubscriptions;

public sealed partial class ParkedMessages : ComponentBase, IDisposable {
	[Inject] PersistentSubscriptionsService PersistentSubscriptionsService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string StreamId { get; set; }
	[Parameter] public string GroupName { get; set; }

	readonly StreamPageCursor _cursor = new(20);
	IReadOnlyList<ResolvedEvent> _events = [];
	bool _loading = true;
	ClaimsPrincipal _principal;

	bool IsLeader => Gossip.CurrentState == VNodeState.Leader;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		StreamId = Uri.UnescapeDataString(StreamId);
		GroupName = Uri.UnescapeDataString(GroupName);
		Gossip.Changed += OnGossipChanged;
		await LoadPage();
	}

	async void OnGossipChanged() {
		try {
			await InvokeAsync(StateHasChanged);
		} catch (ObjectDisposedException) {
		}
	}

	public void Dispose() => Gossip.Changed -= OnGossipChanged;

	async Task LoadPage() {
		_loading = true;
		try {
			using var cts = new CancellationTokenSource(5000);
			var (events, isEnd, _) = await PersistentSubscriptionsService.GetParkedMessagesAsync(
				_principal, StreamId, GroupName, _cursor.From, _cursor.PageSize, cts.Token);
			_events = events;
			_cursor.OnLoaded(
				events.Count > 0 ? events[0].OriginalEventNumber : null,
				events.Count > 0 ? events[^1].OriginalEventNumber : null,
				isEnd);
		} catch (PersistentSubscriptionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		} catch (Exception) {
			// Transient
		} finally {
			_loading = false;
		}
	}

	async Task NewestPage() { _cursor.ToNewest(); await LoadPage(); }
	async Task OldestPage() { _cursor.ToOldest(); await LoadPage(); }
	async Task NextPage() { _cursor.ToOlder(); await LoadPage(); }      // older
	async Task PreviousPage() { _cursor.ToNewer(); await LoadPage(); }  // newer

	async Task OnPageSizeChanged(int value) {
		_cursor.ChangePageSize(value);
		await LoadPage();
	}

	async Task ReplayParked() {
		var confirmed = await DialogService.ShowMessageBox(
			"Replay Parked Messages",
			$"Are you sure you want to replay all parked messages for '{GroupName}' on '{StreamId}'?",
			yesText: "Replay", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(10000);
				await PersistentSubscriptionsService.ReplayParkedAsync(_principal, StreamId, GroupName, cts.Token);
				Snackbar.Add("Parked messages replay started.", Severity.Success);
				await LoadPage();
			} catch (PersistentSubscriptionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task ReplayInclusive(ResolvedEvent parkedEvent) {
		var eventNumber = parkedEvent.OriginalEventNumber;
		var confirmed = await DialogService.ShowMessageBox(
			"Replay Parked Messages",
			$"Are you sure you want to replay all parked messages up to and including #{eventNumber} for '{GroupName}' on '{StreamId}'?",
			yesText: "Replay", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(10000);
				await PersistentSubscriptionsService.ReplayParkedAsync(_principal, StreamId, GroupName, cts.Token, stopAt: eventNumber + 1);
				_events = _events.Where(e => e.OriginalEventNumber > eventNumber).ToList();
				Snackbar.Add("Parked messages replay started.", Severity.Success);
			} catch (PersistentSubscriptionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task TruncateInclusive(ResolvedEvent parkedEvent) {
		var eventNumber = parkedEvent.OriginalEventNumber;
		var confirmed = await DialogService.ShowMessageBox(
			"Truncate Parked Messages",
			$"Are you sure you want to truncate all parked messages up to and including #{eventNumber} for '{GroupName}' on '{StreamId}'?",
			yesText: "Truncate", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(10000);
				await PersistentSubscriptionsService.TruncateParkedAsync(_principal, StreamId, GroupName, cts.Token, stopAt: eventNumber + 1);
				_events = _events.Where(e => e.OriginalEventNumber > eventNumber).ToList();
				Snackbar.Add("Parked messages truncation started.", Severity.Success);
			} catch (PersistentSubscriptionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task TruncateParked() {
		var confirmed = await DialogService.ShowMessageBox(
			"Truncate Parked Messages",
			$"Are you sure you want to truncate all parked messages for '{GroupName}' on '{StreamId}'? This will permanently discard them without replaying.",
			yesText: "Truncate", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(10000);
				await PersistentSubscriptionsService.TruncateParkedAsync(_principal, StreamId, GroupName, cts.Token);
				_events = [];
				Snackbar.Add("Parked messages truncation started.", Severity.Success);
			} catch (PersistentSubscriptionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}
}
