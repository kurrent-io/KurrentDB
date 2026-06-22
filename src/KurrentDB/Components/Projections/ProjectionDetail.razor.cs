// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace KurrentDB.Components.Projections;

public sealed partial class ProjectionDetail : ComponentBase, IDisposable {
	[Inject] ProjectionsService ProjectionsService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string Name { get; set; }

	// Projections only run on the leader, so the management actions are only offered there. On a follower we
	// hide them and link to this projection on the leader (the read-only stats below still render anywhere).
	bool IsLeader => Gossip.CurrentState == VNodeState.Leader;
	string LeaderUrl => Gossip.CurrentState is { } s && s != VNodeState.Leader && Gossip.LeaderEndpoint is { } e
		? $"{new Uri(Navigation.BaseUri).Scheme}://{e.Ip}:{e.Port}/ui/projections/{Uri.EscapeDataString(Name)}"
		: null;

	ProjectionStatistics _stats;
	string _query = "";
	string _state = "";
	string _partition = "";
	bool _loading = true;
	Timer _timer;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		Name = Uri.UnescapeDataString(Name);
		// Re-render promptly on a failover so the management actions appear/disappear with the role.
		Gossip.Changed += OnGossipChanged;
		await LoadAll();
		_timer = new(Callback, null, 5000, 5000);
	}

	async void OnGossipChanged() {
		try {
			await InvokeAsync(StateHasChanged);
		} catch (ObjectDisposedException) {
			// Renderer torn down between the gossip change and this callback — ignore.
		}
	}

	void Callback(object state) {
		Task.Run(async () => {
			await RefreshStats();
			try {
				await InvokeAsync(StateHasChanged);
			} catch (ObjectDisposedException) {
				// Component/renderer torn down between ticks — ignore.
			}
		});
	}

	async Task LoadAll() {
		await RefreshStats();
		try {
			using var cts = new CancellationTokenSource(5000);
			_query = await ProjectionsService.GetQueryAsync(_principal, Name, cts.Token);
		} catch { /* ignore */ }
		try {
			using var cts = new CancellationTokenSource(5000);
			_state = await ProjectionsService.GetStateAsync(_principal, Name, cts.Token, _partition?.Trim() ?? "");
		} catch { /* ignore */ }
		_loading = false;
	}

	// Load the state for the entered partition (blank = the projection's root state).
	async Task LoadState() {
		try {
			using var cts = new CancellationTokenSource(5000);
			_state = await ProjectionsService.GetStateAsync(_principal, Name, cts.Token, _partition?.Trim() ?? "");
		} catch (ProjectionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	async Task OnPartitionKeyUp(KeyboardEventArgs e) {
		if (e.Key == "Enter")
			await LoadState();
	}

	async Task RefreshStats() {
		try {
			using var cts = new CancellationTokenSource(5000);
			var stats = await ProjectionsService.GetStatisticsAsync(_principal, Name, cts.Token);
			_stats = stats.FirstOrDefault();
		} catch { /* ignore */ }
	}

	async Task Enable() {
		try {
			using var cts = new CancellationTokenSource(5000);
			await ProjectionsService.EnableAsync(_principal, Name, cts.Token);
			Snackbar.Add($"Projection '{Name}' started.", Severity.Success);
			await RefreshStats();
		} catch (ProjectionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	async Task Disable() {
		try {
			using var cts = new CancellationTokenSource(5000);
			await ProjectionsService.DisableAsync(_principal, Name, cts.Token);
			Snackbar.Add($"Projection '{Name}' stopped.", Severity.Success);
			await RefreshStats();
		} catch (ProjectionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	async Task Reset() {
		var confirmed = await DialogService.ShowMessageBox(
			"Reset Projection",
			$"Are you sure you want to reset projection '{Name}'?",
			yesText: "Reset", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(5000);
				await ProjectionsService.ResetAsync(_principal, Name, cts.Token);
				Snackbar.Add($"Projection '{Name}' reset.", Severity.Success);
				await LoadAll();
			} catch (ProjectionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task Delete() {
		var confirmed = await DialogService.ShowMessageBox(
			"Delete Projection",
			$"Are you sure you want to delete projection '{Name}'?",
			yesText: "Delete", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(5000);
				await ProjectionsService.DeleteAsync(
					principal: _principal,
					name: Name,
					deleteCheckpointStream: true,
					deleteStateStream: true,
					deleteEmittedStreams: true,
					ct: cts.Token);
				Snackbar.Add($"Projection '{Name}' deleted.", Severity.Success);
				Navigation.NavigateTo("/ui/projections");
			} catch (ProjectionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task EditQuery() {
		var parameters = new DialogParameters<EditProjectionQueryDialog> {
			{ x => x.Name, Name },
			{ x => x.Query, _query },
			{ x => x.Principal, _principal }
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseOnEscapeKey = true };
		var dialog = await DialogService.ShowAsync<EditProjectionQueryDialog>("Edit Source", parameters, options);
		var result = await dialog.Result;
		if (result is { Canceled: false }) {
			Snackbar.Add($"Source for '{Name}' updated.", Severity.Success);
			await LoadAll();
		}
	}

	async Task EditConfig() {
		var parameters = new DialogParameters<ProjectionConfigDialog> {
			{ x => x.Name, Name },
			{ x => x.Principal, _principal },
			{ x => x.EngineVersion, _stats?.EngineVersion ?? 1 }
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };
		var dialog = await DialogService.ShowAsync<ProjectionConfigDialog>("Configuration", parameters, options);
		var result = await dialog.Result;
		if (result is { Canceled: false }) {
			Snackbar.Add($"Configuration for '{Name}' updated.", Severity.Success);
			await RefreshStats();
		}
	}

	public void Dispose() {
		Gossip.Changed -= OnGossipChanged;
		_timer?.Dispose();
	}
}
