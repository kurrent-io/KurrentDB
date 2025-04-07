// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.Services.Monitoring.Utils;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Transport.Tcp;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Monitoring;

public class SystemStatsHelper : IDisposable {
	private readonly ILogger _log;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly string _dbPath;
	private readonly EventCountersHelper _eventCountersHelper;
	private readonly long _totalMem;
	private bool _giveup;

	public SystemStatsHelper(ILogger log, IReadOnlyCheckpoint writerCheckpoint, string dbPath, long collectIntervalMs) {
		Ensure.NotNull(log, "log");
		Ensure.NotNull(writerCheckpoint, "writerCheckpoint");

		_log = log;
		_writerCheckpoint = writerCheckpoint;
		_eventCountersHelper = new EventCountersHelper(collectIntervalMs);
		_dbPath = dbPath;
		_totalMem = RuntimeStats.GetTotalMemory();
	}

	public void Start() => _eventCountersHelper.Start();

	public IDictionary<string, object> GetSystemStats() {
		var stats = new Dictionary<string, object>();
		GetPerfCounterInformation(stats, 0);

		var diskIo = ProcessStats.GetDiskIo();

		stats["proc-diskIo-readBytes"] = diskIo.ReadBytes;
		stats["proc-diskIo-writtenBytes"] = diskIo.WrittenBytes;
		stats["proc-diskIo-readOps"] = diskIo.ReadOps;
		stats["proc-diskIo-writeOps"] = diskIo.WriteOps;

		var tcp = TcpConnectionMonitor.Default.GetTcpStats();
		stats["proc-tcp-connections"] = tcp.Connections;
		stats["proc-tcp-receivingSpeed"] = tcp.ReceivingSpeed;
		stats["proc-tcp-sendingSpeed"] = tcp.SendingSpeed;
		stats["proc-tcp-inSend"] = tcp.InSend;
		stats["proc-tcp-measureTime"] = tcp.MeasureTime;
		stats["proc-tcp-pendingReceived"] = tcp.PendingReceived;
		stats["proc-tcp-pendingSend"] = tcp.PendingSend;
		stats["proc-tcp-receivedBytesSinceLastRun"] = tcp.ReceivedBytesSinceLastRun;
		stats["proc-tcp-receivedBytesTotal"] = tcp.ReceivedBytesTotal;
		stats["proc-tcp-sentBytesSinceLastRun"] = tcp.SentBytesSinceLastRun;
		stats["proc-tcp-sentBytesTotal"] = tcp.SentBytesTotal;

		stats["es-checksum"] = _writerCheckpoint.Read();
		stats["es-checksumNonFlushed"] = _writerCheckpoint.ReadNonFlushed();

		var drive = DriveStats.GetDriveInfo(_dbPath);

		Func<string, string, string> driveStat = (diskName, stat) => $"sys-drive-{diskName.Replace("\\", "").Replace(":", "")}-{stat}";
		stats[driveStat(drive.DiskName, "availableBytes")] = drive.AvailableBytes;
		stats[driveStat(drive.DiskName, "totalBytes")] = drive.TotalBytes;
		stats[driveStat(drive.DiskName, "usage")] = drive.Usage;
		stats[driveStat(drive.DiskName, "usedBytes")] = drive.UsedBytes;

		Func<string, string, string> queueStat = (queueName, stat) =>
			string.Format("es-queue-{0}-{1}", queueName, stat);
		var queues = QueueMonitor.Default.GetStats();
		foreach (var queue in queues) {
			stats[queueStat(queue.Name, "queueName")] = queue.Name;
			stats[queueStat(queue.Name, "groupName")] = queue.GroupName ?? string.Empty;
			stats[queueStat(queue.Name, "avgItemsPerSecond")] = queue.AvgItemsPerSecond;
			stats[queueStat(queue.Name, "avgProcessingTime")] = queue.AvgProcessingTime;
			stats[queueStat(queue.Name, "currentIdleTime")] = queue.CurrentIdleTime.HasValue
				? queue.CurrentIdleTime.Value.ToString("G", CultureInfo.InvariantCulture)
				: null;
			stats[queueStat(queue.Name, "currentItemProcessingTime")] = queue.CurrentItemProcessingTime.HasValue
				? queue.CurrentItemProcessingTime.Value.ToString("G", CultureInfo.InvariantCulture)
				: null;
			stats[queueStat(queue.Name, "idleTimePercent")] = queue.IdleTimePercent;
			stats[queueStat(queue.Name, "length")] = queue.Length;
			stats[queueStat(queue.Name, "lengthCurrentTryPeak")] = queue.LengthCurrentTryPeak;
			stats[queueStat(queue.Name, "lengthLifetimePeak")] = queue.LengthLifetimePeak;
			stats[queueStat(queue.Name, "totalItemsProcessed")] = queue.TotalItemsProcessed;
			stats[queueStat(queue.Name, "inProgressMessage")] = queue.InProgressMessageType != null
				? queue.InProgressMessageType.Name
				: "<none>";
			stats[queueStat(queue.Name, "lastProcessedMessage")] = queue.LastProcessedMessageType != null
				? queue.LastProcessedMessageType.Name
				: "<none>";
		}

		return stats;
	}

	private void GetPerfCounterInformation(Dictionary<string, object> stats, int count) {
		if (_giveup)
			return;
		var process = Process.GetCurrentProcess();
		try {
			stats["proc-startTime"] = process.StartTime.ToUniversalTime().ToString("O");
			stats["proc-id"] = process.Id;
			stats["proc-mem"] = new StatMetadata(process.WorkingSet64, "Process", "Process Virtual Memory");
			stats["proc-cpu"] = new StatMetadata(_eventCountersHelper.GetProcCpuUsage(), "Process", "Process Cpu Usage");
			stats["proc-threadsCount"] = _eventCountersHelper.GetProcThreadsCount();
			stats["proc-contentionsRate"] = _eventCountersHelper.GetContentionsRateCount();
			stats["proc-thrownExceptionsRate"] = _eventCountersHelper.GetThrownExceptionsRate();

			stats["sys-cpu"] = RuntimeStats.GetCpuUsage();

			if (RuntimeInformation.IsUnix) {
				var loadAverages = RuntimeStats.GetCpuLoadAverages();
				stats["sys-loadavg-1m"] = loadAverages.OneMinute;
				stats["sys-loadavg-5m"] = loadAverages.FiveMinutes;
				stats["sys-loadavg-15m"] = loadAverages.FifteenMinutes;
			}

			stats["sys-freeMem"] = RuntimeStats.GetFreeMemory();
			stats["sys-totalMem"] = _totalMem;

			var gcStats = _eventCountersHelper.GetGcStats();
			stats["proc-gc-allocationSpeed"] = gcStats.AllocationSpeed;
			stats["proc-gc-fragmentation"] = gcStats.Fragmentation;
			stats["proc-gc-gen0ItemsCount"] = gcStats.Gen0ItemsCount;
			stats["proc-gc-gen0Size"] = gcStats.Gen0Size;
			stats["proc-gc-gen1ItemsCount"] = gcStats.Gen1ItemsCount;
			stats["proc-gc-gen1Size"] = gcStats.Gen1Size;
			stats["proc-gc-gen2ItemsCount"] = gcStats.Gen2ItemsCount;
			stats["proc-gc-gen2Size"] = gcStats.Gen2Size;
			stats["proc-gc-largeHeapSize"] = gcStats.LargeHeapSize;
			stats["proc-gc-timeInGc"] = gcStats.TimeInGc;
			stats["proc-gc-totalBytesInHeaps"] = gcStats.TotalBytesInHeaps;
		} catch (InvalidOperationException) {
			_log.Information("Received error reading counters. Attempting to rebuild.");
			_giveup = count > 10;
			if (_giveup)
				_log.Error("Maximum rebuild attempts reached. Giving up on rebuilds.");
			else
				GetPerfCounterInformation(stats, count + 1);
		}
	}

	public void Dispose() {
		_eventCountersHelper.Dispose();
	}
}
