// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Licensed;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Cluster;
using KurrentDB.KontrolPlane.Raft;
using KurrentDB.Tools;
using KurrentDB.UI.Theme;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using ChartSeries = KurrentDB.Tools.ChartSeries;

namespace KurrentDB.Components.Cluster;

public sealed partial class Cluster : WithLicense, IDisposable {
	[Inject] MonitoringService MonitoringService { get; set; } = null!;
	[Inject] Core.Metrics.InternalExporter InternalExporter { get; set; } = null!;
	[Inject] ClusterOperationsService ClusterOperationsService { get; set; } = null!;
	[Inject] KontrolPlaneService KontrolPlaneService { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] IAuthorizationProvider Authorizer { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	ClientClusterInfo _clusterInfo;
	KontrollerClusterInfo? _kontrolPlaneInfo;
	Timer _timer;
	bool _canShutdown;
	bool _canResign;

	protected override async Task OnInitializedAsync() {
		await base.OnInitializedAsync();
		// Show Shutdown/Resign to everyone but enable them only for users the policy allows; the same
		// operations are also enforced server-side by ClusterOperationsService. Node.Shutdown/Resign are
		// claim-based, so this is a cheap one-time check.
		var user = (await AuthenticationState).User;
		_canShutdown = await Authorizer.CheckAccessAsync(user, new Operation(Operations.Node.Shutdown), default);
		_canResign = await Authorizer.CheckAccessAsync(user, new Operation(Operations.Node.Resign), default);
		_cpuChart = _cpuSeries.Add("CPU %");
		_ramChart = _ramSeries.Add("RAM %");
		_eventsRead = _eventsSeries.Add("Records read");
		_eventsWritten = _eventsSeries.Add("Records written");
		_eventBytesRead = _eventBytesSeries.Add("Record kb read");
		_eventBytesWritten = _eventBytesSeries.Add("Record kb written");

		_timer = new(Callback, null, 0, 1000);
	}

	async Task RefreshStatus() {
		try {
			using var cts = new CancellationTokenSource(1000);
			_clusterInfo = await ClusterOperationsService.GetClusterInfoAsync(cts.Token);
		} catch (Exception) {
			// Gossip timed out or the node is shutting down — keep the last known cluster info.
		}

		// Local and non-blocking, so it needs no timeout of its own.
		_kontrolPlaneInfo = KontrolPlaneService.GetClusterInfo();
	}

	void Callback(object state) {
		Task.Run(RefreshStatus);
		InternalExporter.Collect?.Invoke(100);
		_cpu = MonitoringService.CalculateCpu() * 100;
		var ram = MonitoringService.CalculateRam();
		_ram = ram.Total > 0 ? ram.Used / ram.Total * 100 : 0;
		_cpuChart.AddData(_cpu);
		_ramChart.AddData(_ram);

		_disk = (double)InternalExporter.Snapshot.DiskUsedBytes / InternalExporter.Snapshot.DiskTotalBytes * 100;

		_eventsRead.AddData(InternalExporter.Snapshot.EventsRead);
		_eventsWritten.AddData(InternalExporter.Snapshot.EventsWritten);
		_eventBytesRead.AddData((double)InternalExporter.Snapshot.EventBytesRead / 1024);
		_eventBytesWritten.AddData((double)InternalExporter.Snapshot.EventBytesWritten / 1024);

		Refresh();
	}

	void Refresh() {
		Task.Run(async () => {
			try {
				await InvokeAsync(StateHasChanged);
			} catch (ObjectDisposedException) {
				// Component/renderer torn down between timer ticks — ignore.
			}
		});
	}

	// Gossip and the Kontroller each report members in whatever order they happen to hold them, which
	// is not stable between refreshes, so sort both to keep the cards in place.
	IEnumerable<ClientClusterInfo.ClientMemberInfo> SortedMembers {
		get {
			ClientClusterInfo.ClientMemberInfo[] members = _clusterInfo?.Members ?? [];
			return members
				.OrderBy(x => x.HttpEndPointIp, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.HttpEndPointPort);
		}
	}

	IEnumerable<KontrollerNodeInfo> SortedKontrolPlaneNodes {
		get {
			IReadOnlyList<KontrollerNodeInfo> nodes = _kontrolPlaneInfo?.Nodes ?? [];
			return nodes
				.OrderBy(x => x.EndPoint.GetHost(), StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.EndPoint.GetPort());
		}
	}

	KontrollerNodeInfo? KontrolPlaneLeader {
		get {
			foreach (var node in _kontrolPlaneInfo?.Nodes ?? [])
				if (node.IsLeader)
					return node;

			return null;
		}
	}

	// Only the Kontrol Plane leader has a complete picture: a follower does not contact its peers
	// between elections, so it cannot report their status and shows them as Unknown.
	bool IsKontrolPlaneLeader => KontrolPlaneLeader is { IsRemote: false };

	// A Kontroller is known by its Raft address, while its UI is on the node's HTTP endpoint, so the
	// two are matched by host through gossip. Deliberately no link when the host does not identify a
	// single member - guessing a port would send people to the wrong node.
	string KontrolPlaneLeaderUrl {
		get {
			if (KontrolPlaneLeader is not { } leader)
				return null;

			var host = leader.EndPoint.GetHost();
			ClientClusterInfo.ClientMemberInfo match = null;
			foreach (var member in _clusterInfo?.Members ?? []) {
				if (!string.Equals(member.HttpEndPointIp, host, StringComparison.OrdinalIgnoreCase))
					continue;

				if (match is not null)
					return null;

				match = member;
			}

			return match is null
				? null
				: $"{new Uri(Navigation.BaseUri).Scheme}://{match.HttpEndPointIp}:{match.HttpEndPointPort}/ui/cluster";
		}
	}

	readonly ChartOptions _options = new() {
		YAxisLines = false,
		YAxisTicks = 100,
		MaxNumYAxisTicks = 10,
		YAxisRequireZeroPoint = true,
		XAxisLines = false,
		LineStrokeWidth = 1,
		ChartPalette = KurrentTheme.ChartPalette,
	};

	// System graphs
	readonly ChartSeries _cpuSeries = [];
	ChartSeriesValues _cpuChart;
	readonly ChartSeries _ramSeries = [];
	ChartSeriesValues _ramChart;

	// Events graph
	readonly ChartSeries _eventsSeries = [];
	ChartSeriesValues _eventsRead;
	ChartSeriesValues _eventsWritten;
	readonly ChartSeries _eventBytesSeries = [];
	ChartSeriesValues _eventBytesRead;
	ChartSeriesValues _eventBytesWritten;

	int _index = -1;

	bool IsClusterHealthy => _clusterInfo?.Members?.All(x => x.IsAlive) ?? true;
	string ClusterIcon => IsClusterHealthy ? Icons.Material.Filled.Check : Icons.Material.Filled.Warning;
	Color ClusterIconColor => IsClusterHealthy ? Color.Primary : Color.Warning;
	bool IsCluster => (_clusterInfo?.Members?.Length ?? 0) > 1;
	static string LocalOs => RuntimeInformation.OSDescription;
	double _cpu;
	double _ram;
	double _disk;

	bool IsLocalNode(ClientClusterInfo.ClientMemberInfo member) =>
		member.HttpEndPointIp == _clusterInfo?.ServerIp && member.HttpEndPointPort == _clusterInfo?.ServerPort;

	// The serving node — the one whose Shutdown/Resign buttons appear in the Node status section.
	ClientClusterInfo.ClientMemberInfo LocalNode => _clusterInfo?.Members?.FirstOrDefault(IsLocalNode);

	async Task ShutdownServer() {
		// Shutdown only targets the serving (local) node — the only card that shows the button — so name it.
		var node = $"{_clusterInfo?.ServerIp}:{_clusterInfo?.ServerPort}";
		var confirmed = await DialogService.ShowMessageBox(
			"Shutdown Server",
			$"Are you sure you want to shut down node {node}? This will terminate the KurrentDB process.",
			yesText: "Shutdown", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				var principal = (await AuthenticationState).User;
				await ClusterOperationsService.ShutdownAsync(principal, default);
				Snackbar.Add("Server shutdown initiated.", Severity.Warning);
			} catch (OperationsAccessDeniedException) {
				Snackbar.Add("Access denied.", Severity.Error);
			}
		}
	}

	async Task ResignNode() {
		var confirmed = await DialogService.ShowMessageBox(
			"Resign Node",
			"Are you sure you want this node to resign from leadership?",
			yesText: "Resign", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				var principal = (await AuthenticationState).User;
				await ClusterOperationsService.ResignNodeAsync(principal, default);
				Snackbar.Add("Node resignation initiated.", Severity.Info);
			} catch (OperationsAccessDeniedException) {
				Snackbar.Add("Access denied.", Severity.Error);
			}
		}
	}

	public void Dispose() {
		_timer?.Dispose();
	}
}
