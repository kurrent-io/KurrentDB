// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.NetworkInformation;

namespace EventStore.Licensing.Keygen;

// Identifies this ESDB node
// We want the fingerprint to be stable across restarts so that multiple restarts do
// not use up the machine overage.
// Each keygen machine object is associated with a single license.
// Using another license with the same fingerprint will try to create another keygen machine object.
// This can be rejected by the policy, but if it is permitted, both machines will have the same fingerprint.
public class Fingerprint {
	readonly string _nodeId;
	private readonly int? _port;

	public Fingerprint(int? port) {
		var mac = NetworkInterface.GetAllNetworkInterfaces()
			.FirstOrDefault(nic => nic.OperationalStatus == OperationalStatus.Up
								   && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			?.GetPhysicalAddress()?.ToString();
		_nodeId = mac ?? Guid.NewGuid().ToString();
		_port = port;
	}

	public static long Ram => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

	public static int CpuCount => Environment.ProcessorCount;

	public string Get() {
		var fingerPrint = $"{_nodeId} {CpuCount} {Ram}";
		return _port is null ? fingerPrint : $"{_port} {fingerPrint}";
	}
}
