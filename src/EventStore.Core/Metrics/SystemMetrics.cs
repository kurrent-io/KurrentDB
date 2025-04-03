// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime;
using EventStore.Core.Util;
using static EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.Metrics;

public class SystemMetrics(Meter meter, TimeSpan timeout, Dictionary<SystemTracker, bool> config, bool legacyNames) {
	public void CreateLoadAverageMetric(string metricName, Dictionary<SystemTracker, string> dimNames) {
		if (RuntimeInformation.IsWindows)
			return;

		var dims = new Dimensions<SystemTracker, double>(config, dimNames, tag => new("period", tag));

		var getLoadAverages = Functions.Debounce(RuntimeStats.GetCpuLoadAverages, timeout);
		dims.Register(SystemTracker.LoadAverage1m, () => getLoadAverages().OneMinute);
		dims.Register(SystemTracker.LoadAverage5m, () => getLoadAverages().FiveMinutes);
		dims.Register(SystemTracker.LoadAverage15m, () => getLoadAverages().FifteenMinutes);

		if (dims.AnyRegistered())
			meter.CreateObservableGauge(metricName, dims.GenObserve());
	}

	public void CreateCpuMetric(string name) {
		if (!config.TryGetValue(SystemTracker.Cpu, out var enabled) || !enabled)
			return;

		meter.CreateObservableUpDownCounter(name, RuntimeStats.GetCpuUsage);
	}

	public void CreateMemoryMetric(string metricName, Dictionary<SystemTracker, string> dimNames) {
		var dims = new Dimensions<SystemTracker, long>(config, dimNames, tag => new("kind", tag));

		dims.Register(SystemTracker.FreeMem, RuntimeStats.GetFreeMemory);
		dims.Register(SystemTracker.TotalMem, RuntimeStats.GetTotalMemory);

		if (dims.AnyRegistered())
			if (legacyNames)
				meter.CreateObservableGauge($"{metricName}-bytes", dims.GenObserve());
			else
				meter.CreateObservableGauge(metricName, dims.GenObserve(), "bytes");
	}

	public void CreateDiskMetric(string metricName, string dbPath, Dictionary<SystemTracker, string> dimNames) {
		var dims = new Dimensions<SystemTracker, long>(config, dimNames, tag => new());

		var getDriveInfo = Functions.Debounce(() => DriveStats.GetDriveInfo(dbPath), timeout);

		dims.Register(SystemTracker.DriveUsedBytes, GenMeasure(info => info.UsedBytes));
		dims.Register(SystemTracker.DriveTotalBytes, GenMeasure(info => info.TotalBytes));

		if (dims.AnyRegistered())
			if (legacyNames)
				meter.CreateObservableGauge($"{metricName}-bytes", dims.GenObserve());
			else
				meter.CreateObservableGauge(metricName, dims.GenObserve(), "bytes");
		
		return;

		Func<string, Measurement<long>> GenMeasure(Func<DriveData, long> func) => tag => {
			var info = getDriveInfo();

			return new(
				func(info),
				new("kind", tag),
				new("disk", info.DiskName)
			);
		};
	}
}
