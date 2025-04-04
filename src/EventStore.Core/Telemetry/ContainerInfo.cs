using System;
using System.IO;
using EventStore.Common.Utils;

namespace EventStore.Core.Telemetry;

public class ContainerInfo {
	public static ContainerInfo Instance { get; } = Collect();
	public bool IsContainer { get; init; }
	public bool IsKubernetes { get; private set; }

	private static ContainerInfo Collect() {
		var info = new ContainerInfo {
			IsContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null,
		};

		if (OS.OsFlavor != OsFlavor.Linux || !File.Exists("/proc/self/cgroup"))
			return info;

		try {
			string cgroup = File.ReadAllText("/proc/self/cgroup");
			info.IsKubernetes = cgroup.Contains("kubepods");
		} catch (Exception) {
			// ignored
		}

		return info;
	}
}
