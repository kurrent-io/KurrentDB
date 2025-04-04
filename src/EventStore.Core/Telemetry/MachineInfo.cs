using System;
using EventStore.Common.Utils;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Native.Monitoring;

namespace EventStore.Core.Telemetry;

public class MachineInfo {
	public int ProcessorCount { get; set; }
	public long TotalMemory { get; set; }
	public long TotalDiskSpace { get; set; }

	public static MachineInfo Collect(ClusterVNodeOptions options) {
		var diskInfo = EsDriveInfo.FromDirectory(options.Database.Db, Serilog.Log.Logger);
		return new MachineInfo {
			ProcessorCount = Environment.ProcessorCount,
			TotalMemory = GetTotalMemory(),
			TotalDiskSpace = diskInfo?.TotalBytes ?? 0,
		};
	}

	private static long GetTotalMemory() {
		if (OS.IsUnix) {
			var stats = new HostStat.HostStat();
			return (long)stats.GetTotalMemory();
		}

		if (OS.OsFlavor == OsFlavor.Windows) {
			return (long)WinNativeMemoryStatus.GetTotalMemory();
		}

		return -1;
	}
}
