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
using KurrentDB.Transport.Tcp;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Monitoring;

public class SystemStatsHelper : IDisposable {
	private readonly ILogger _log;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly string _dbPath;
	private readonly EventCountersHelper _eventCountersHelper;
	private readonly long _totalMem = RuntimeStats.GetTotalMemory();
	private bool _giveUp;

	public SystemStatsHelper(ILogger log, IReadOnlyCheckpoint writerCheckpoint, string dbPath, long collectIntervalMs) {
		_dbPath = dbPath;
		_log = Ensure.NotNull(log);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_eventCountersHelper = new(collectIntervalMs);
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

		stats[DriveStat(drive.DiskName, "availableBytes")] = drive.AvailableBytes;
		stats[DriveStat(drive.DiskName, "totalBytes")] = drive.TotalBytes;
		stats[DriveStat(drive.DiskName, "usage")] = drive.Usage;
		stats[DriveStat(drive.DiskName, "usedBytes")] = drive.UsedBytes;

		var queues = QueueMonitor.Default.GetStats();
		foreach (var queue in queues) {
			stats[QueueStat(queue.Name, "queueName")] = queue.Name;
			stats[QueueStat(queue.Name, "groupName")] = queue.GroupName ?? string.Empty;
			stats[QueueStat(queue.Name, "avgItemsPerSecond")] = queue.AvgItemsPerSecond;
			stats[QueueStat(queue.Name, "avgProcessingTime")] = queue.AvgProcessingTime;
			stats[QueueStat(queue.Name, "currentIdleTime")] = queue.CurrentIdleTime?.ToString("G", CultureInfo.InvariantCulture);
			stats[QueueStat(queue.Name, "currentItemProcessingTime")] = queue.CurrentItemProcessingTime?.ToString("G", CultureInfo.InvariantCulture);
			stats[QueueStat(queue.Name, "idleTimePercent")] = queue.IdleTimePercent;
			stats[QueueStat(queue.Name, "length")] = queue.Length;
			stats[QueueStat(queue.Name, "lengthCurrentTryPeak")] = queue.LengthCurrentTryPeak;
			stats[QueueStat(queue.Name, "lengthLifetimePeak")] = queue.LengthLifetimePeak;
			stats[QueueStat(queue.Name, "totalItemsProcessed")] = queue.TotalItemsProcessed;
			stats[QueueStat(queue.Name, "inProgressMessage")] = queue.InProgressMessageType?.Name ?? "<none>";
			stats[QueueStat(queue.Name, "lastProcessedMessage")] = queue.LastProcessedMessageType?.Name ?? "<none>";
		}

		return stats;

		string QueueStat(string queueName, string stat) => $"es-queue-{queueName}-{stat}";

		string DriveStat(string diskName, string stat) => $"sys-drive-{diskName.Replace("\\", "").Replace(":", "")}-{stat}";
	}

	private void GetPerfCounterInformation(Dictionary<string, object> stats, int count) {
		if (_giveUp)
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
			_giveUp = count > 10;
			if (_giveUp)
				_log.Error("Maximum rebuild attempts reached. Giving up on rebuilds.");
			else
				GetPerfCounterInformation(stats, count + 1);
		}
	}

	public void Dispose() {
		_eventCountersHelper.Dispose();
		GC.SuppressFinalize(this);
	}
}
