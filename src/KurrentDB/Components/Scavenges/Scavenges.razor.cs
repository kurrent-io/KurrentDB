// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.Scavenges;

public sealed partial class Scavenges : ComponentBase, IDisposable {
	[Inject] ScavengeService ScavengeService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	ScavengeHistoryEntry[] _history = [];
	bool _loading = true;
	Timer _timer;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		await RefreshHistory();
		_timer = new(Callback, null, 5000, 5000);
	}

	void Callback(object state) {
		Task.Run(async () => {
			await RefreshHistory();
			try {
				await InvokeAsync(StateHasChanged);
			} catch (ObjectDisposedException) {
				// Component/renderer torn down between ticks — ignore.
			}
		});
	}

	async Task RefreshHistory() {
		try {
			using var cts = new CancellationTokenSource(5000);
			_history = await ScavengeService.GetHistoryAsync(_principal, cts.Token);
		} catch (ScavengeException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		} catch (Exception) {
			// Keep showing stale data on transient failures
		} finally {
			_loading = false;
		}
	}

	async Task StartScavenge() {
		var confirmed = await DialogService.ShowMessageBox(
			"Start Scavenge",
			"Are you sure you want to start a database scavenge?",
			yesText: "Start", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(10000);
				var scavengeId = await ScavengeService.StartAsync(_principal, cts.Token);
				Snackbar.Add($"Scavenge '{scavengeId}' started.", Severity.Success);
				await RefreshHistory();
			} catch (ScavengeException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task StopScavenge(string scavengeId) {
		var confirmed = await DialogService.ShowMessageBox(
			"Stop Scavenge",
			$"Are you sure you want to stop scavenge '{scavengeId}'?",
			yesText: "Stop", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(5000);
				await ScavengeService.StopAsync(_principal, scavengeId, cts.Token);
				Snackbar.Add($"Scavenge '{scavengeId}' stopped.", Severity.Success);
				await RefreshHistory();
			} catch (ScavengeException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	public void Dispose() {
		_timer?.Dispose();
	}
}
