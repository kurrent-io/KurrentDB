// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;

namespace KurrentDB.Services;

public sealed class MonitoringService {
	DateTime _lastTime = DateTime.MinValue;
	readonly Process _process = Process.GetCurrentProcess();
	TimeSpan _lastTotalProcessorTime;
	DateTime _curTime;
	TimeSpan _curTotalProcessorTime;

	public double CalculateCpu() {
		if (_lastTime == DateTime.MinValue) {
			_lastTime = DateTime.Now;
			_lastTotalProcessorTime = _process.TotalProcessorTime;
			return 0;
		}

		_curTime = DateTime.Now;
		_curTotalProcessorTime = _process.TotalProcessorTime;

		double cpuUsage = (_curTotalProcessorTime.TotalMilliseconds - _lastTotalProcessorTime.TotalMilliseconds) / _curTime.Subtract(_lastTime).TotalMilliseconds;

		_lastTime = _curTime;
		_lastTotalProcessorTime = _curTotalProcessorTime;
		return cpuUsage;
	}

	public (double Total, double Used) CalculateRam() {
		var processRam = _process.WorkingSet64;
		var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		var totalGb = (double)total / 1024 / 1024 / 1024;
		var processGb = (double)processRam / 1024 / 1024 / 1024;
		return (totalGb, processGb);
	}
}
