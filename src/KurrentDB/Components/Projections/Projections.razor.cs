// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.Projections;

public sealed partial class Projections : ComponentBase, IDisposable {
	[Inject] ProjectionsService ProjectionsService { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	ProjectionStatistics[] _projections = [];
	bool _loading = true;
	Timer _timer;
	ClaimsPrincipal _principal;

	// Events/sec isn't reported by the server — compute it as a delta between polls (like Angular).
	readonly Dictionary<string, (long Count, DateTime At)> _lastSample = new();
	readonly Dictionary<string, double> _rates = new();

	double RateFor(string name) => _rates.TryGetValue(name, out var r) ? r : 0;

	void UpdateRates() {
		var now = DateTime.UtcNow;
		foreach (var p in _projections) {
			long count = p.EventsProcessedAfterRestart;
			if (_lastSample.TryGetValue(p.Name, out var prev)) {
				var ms = (now - prev.At).TotalMilliseconds;
				var delta = count - prev.Count;
				if (ms > 0)
					_rates[p.Name] = delta > 0 ? 1000.0 * delta / ms : 0; // negative = restarted; show 0
			}
			_lastSample[p.Name] = (count, now);
		}
	}

	// False when projections are disabled on this node (--run-projections=None); the page then shows a calm
	// explanation instead of polling a subsystem that isn't there.
	bool ProjectionsAvailable => ProjectionsService.Available;

	// Projections only run on the leader, so on a follower this page has nothing to manage — we show a notice
	// and a link to the leader's equivalent page instead. The role comes live from GossipMonitor; the
	// leader's URL needs a gossip read (to discover its endpoint). Null when we have no leader to link to.
	string _leaderUrl;
	bool IsLeader => Gossip.CurrentState == VNodeState.Leader;
	// Only show the "go to the leader" notice once we positively know this node isn't the leader and we have a
	// leader to link to — so it never flashes on the leader, during startup, or mid-election.
	bool ShowLeaderNotice => Gossip.CurrentState is { } s && s != VNodeState.Leader && _leaderUrl is not null;
	// Friendly label for this node's role, shown in the "go to the leader" notice.
	string RoleLabel => Gossip.CurrentState is { } s ? NodeStateDisplay.Label(s) : "";

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		if (!ProjectionsAvailable) {
			_loading = false;
			return;
		}
		// React promptly to a failover instead of waiting for the next poll.
		Gossip.Changed += OnGossipChanged;
		_timer = new(Callback, null, 0, 5000);
	}

	void OnGossipChanged() => Callback(null);

	void Callback(object state) {
		Task.Run(async () => {
			await Refresh();
			try {
				await InvokeAsync(StateHasChanged);
			} catch (ObjectDisposedException) {
				// Component/renderer torn down between ticks — ignore.
			}
		});
	}

	string _error;

	async Task Refresh() {
		// Reflect the current leader (from gossip, via the monitor) so a failover updates the link.
		UpdateLeaderUrl();
		if (ShowLeaderNotice) {
			_loading = false;
			return;
		}
		try {
			using var cts = new CancellationTokenSource(5000);
			_projections = await ProjectionsService.GetAllAsync(_principal, cts.Token);
			UpdateRates();
			_error = null;
		} catch (Exception ex) {
			_error = ex.GetType().Name + ": " + ex.Message;
		} finally {
			_loading = false;
		}
	}

	// When this node isn't the leader, point at the leader's equivalent page (its endpoint comes from gossip).
	void UpdateLeaderUrl() {
		_leaderUrl = !IsLeader && Gossip.LeaderEndpoint is { } e
			? $"{new Uri(Navigation.BaseUri).Scheme}://{e.Ip}:{e.Port}/ui/projections"
			: null;
	}

	async Task RestartSubsystem() {
		var confirmed = await DialogService.ShowMessageBox(
			"Restart Subsystem",
			"Restart the projections subsystem?",
			yesText: "Restart", cancelText: "Cancel");
		if (confirmed != true)
			return;
		try {
			using var cts = new CancellationTokenSource(30000);
			await ProjectionsService.RestartSubsystemAsync(_principal, cts.Token);
			Snackbar.Add("Projections subsystem restarting.", Severity.Success);
			await Refresh();
		} catch (ProjectionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	async Task CreateProjection() {
		var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true };
		var dialog = await DialogService.ShowAsync<CreateProjectionDialog>("New Projection", options);
		var result = await dialog.Result;
		if (result is { Canceled: false, Data: CreateProjectionDialog.CreateProjectionModel model }) {
			try {
				using var cts = new CancellationTokenSource(10000);
				await ProjectionsService.CreateAsync(_principal, model.Name, model.Query, model.Mode,
					model.EmitEnabled, model.TrackEmittedStreams, model.UseV2, cts.Token);
				Snackbar.Add($"Projection '{model.Name}' created.", Severity.Success);
				await Refresh();
			} catch (ProjectionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task EnableAll() {
		var confirmed = await DialogService.ShowMessageBox(
			"Enable All Projections",
			"Are you sure you want to enable all projections?",
			yesText: "Enable All", cancelText: "Cancel");
		if (confirmed != true)
			return;
		try {
			using var cts = new CancellationTokenSource(30000);
			await ProjectionsService.EnableAllAsync(_principal, cts.Token);
			Snackbar.Add("All projections enabled.", Severity.Success);
			await Refresh();
		} catch (ProjectionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	async Task DisableAll() {
		var confirmed = await DialogService.ShowMessageBox(
			"Disable All Projections",
			"Are you sure you want to disable all projections?",
			yesText: "Disable All", cancelText: "Cancel");
		if (confirmed != true)
			return;
		try {
			using var cts = new CancellationTokenSource(30000);
			await ProjectionsService.DisableAllAsync(_principal, cts.Token);
			Snackbar.Add("All projections disabled.", Severity.Success);
			await Refresh();
		} catch (ProjectionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	public void Dispose() {
		Gossip.Changed -= OnGossipChanged;
		_timer?.Dispose();
	}
}
