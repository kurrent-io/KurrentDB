// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using KurrentDB.Components.Licensed;
using KurrentDB.Tools;
using MudBlazor;
using ChartSeries = KurrentDB.Tools.ChartSeries;

namespace KurrentDB.Components.Cluster;

public partial class Cluster : WithLicense, IDisposable {
	ClientClusterInfo _clusterInfo;
	Timer _timer;

	protected override async Task OnInitializedAsync() {
		await base.OnInitializedAsync();
		_cpuChart = _cpuSeries.Add("CPU %");
		_ramChart = _ramSeries.Add("RAM %");
		_eventsRead = _eventsSeries.Add("Events read");
		_eventsWritten = _eventsSeries.Add("Events written");
		_eventBytesRead = _eventBytesSeries.Add("Event kb read");
		_eventBytesWritten = _eventBytesSeries.Add("Event kb written");

		_timer = new(Callback, null, 0, 1000);
		MetricsObserver.DataUpdated += MonitoringServiceOnDataUpdated;
		_hasLicense = LicenseService.CurrentLicense != null;
	}

	async Task RefreshStatus() {
		using var cts = new CancellationTokenSource(1000);
		var r = await RequestClient.RequestAsync<GossipMessage.ClientGossip, GossipMessage.SendClientGossip>(Publisher, env => new(env), cts.Token);
		_clusterInfo = r.ClusterInfo;
	}

	void Callback(object state) {
		Task.Run(RefreshStatus);
		InternalExporter.Collect?.Invoke(100);
		_cpu = MonitoringService.CalculateCpu() * 100;
		var ram = MonitoringService.CalculateRam();
		_ram = ram.Used / ram.Total * 100;
		_cpuChart.AddData(_cpu);
		_ramChart.AddData(_ram);

		_disk = (double)InternalExporter.Snapshot.DiskUsedBytes / InternalExporter.Snapshot.DiskTotalBytes * 100;

		_eventsRead.AddData(InternalExporter.Snapshot.EventsRead);
		_eventsWritten.AddData(InternalExporter.Snapshot.EventsWritten);
		_eventBytesRead.AddData((double)InternalExporter.Snapshot.EventBytesRead / 1024);
		_eventBytesWritten.AddData((double)InternalExporter.Snapshot.EventBytesWritten / 1024);

		Refresh();
	}

	void MonitoringServiceOnDataUpdated(object sender, EventArgs e) {
		Refresh();
	}

	void Refresh() {
		Task.Run(() => InvokeAsync(StateHasChanged));
	}

	readonly ChartOptions _options = new() {
		YAxisLines = false,
		YAxisTicks = 100,
		MaxNumYAxisTicks = 10,
		YAxisRequireZeroPoint = true,
		XAxisLines = false,
		LineStrokeWidth = 1,
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
	Color ClusterIconColor => IsClusterHealthy ? Color.Success : Color.Warning;
	int CardWidth => 12 / (_clusterInfo?.Members?.Length ?? 1);
	static string LocalOs => RuntimeInformation.OSDescription;
	double _cpu;
	double _ram;
	double _disk;
	bool _hasLicense;

	public void Dispose() {
		_timer.Dispose();
		MetricsObserver.DataUpdated -= MonitoringServiceOnDataUpdated;
	}
}
