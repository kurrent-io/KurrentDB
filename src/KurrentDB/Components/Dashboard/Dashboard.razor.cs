// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.Dashboard;

public sealed partial class Dashboard : ComponentBase, IDisposable {
	[Inject] DashboardService DashboardService { get; set; } = null!;

	[Inject] IDialogService DialogService { get; set; }
	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	QueueStats[] _queues = [];
	bool _loading = true;
	Timer _timer;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		_timer = new(Callback, null, 0, 2000);
	}

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

	async Task Refresh() {
		try {
			using var cts = new CancellationTokenSource(5000);
			var stats = await DashboardService.GetStatsAsync(_principal, cts.Token);
			_queues = QueueStats.FromDictionary(stats);
		} catch (Exception) {
			// Keep showing stale data on transient failures (including access-denied)
		} finally {
			_loading = false;
		}
	}

	async Task ShowSnapshot() {
		var snapshot = BuildSnapshot();
		var parameters = new DialogParameters<SnapshotDialog> {
			{ x => x.Snapshot, snapshot }
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseOnEscapeKey = true };
		await DialogService.ShowAsync<SnapshotDialog>("Dashboard Snapshot", parameters, options);
	}

	string BuildSnapshot() {
		var sb = new StringBuilder();
		sb.AppendLine($"Snapshot taken at: {DateTime.Now:F}");
		sb.AppendLine();
		sb.AppendLine($"{"Name",-40}  {"Length",10}  {"Rate/s",10}  {"Time(ms)",10}  {"Processed",12}  {"Current Message",-30}  {"Last Message",-30}");
		sb.AppendLine(new string('=', 150));

		foreach (var q in _queues) {
			sb.AppendLine(
				$"{Trim(q.Name, 40),-40}  " +
				$"{q.Length,10:N0}  " +
				$"{q.AvgItemsPerSecond,10:N0}  " +
				$"{q.AvgProcessingTime,10:F3}  " +
				$"{q.TotalItemsProcessed,12:N0}  " +
				$"{Trim(q.CurrentMessage, 30),-30}  " +
				$"{Trim(q.LastMessage, 30),-30}");
		}

		return sb.ToString();

		static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
	}

	public void Dispose() {
		_timer?.Dispose();
	}
}
