// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.NetworkInformation;

namespace EventStore.Common.Utils;

public static class IPFinder {
	public static IPAddress GetNonLoopbackAddress() {
		foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
			foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses) {
				if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
					if (!IPAddress.IsLoopback(address.Address)) {
						return address.Address;
					}
				}
			}
		}

		return null;
	}
}
