// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using EventStore.Common.Utils;

namespace EventStore.Core.Services.Gossip;

public class DnsGossipSeedSource(string hostname, int managerHttpPort) : IGossipSeedSource {
	public IAsyncResult BeginGetHostEndpoints(AsyncCallback requestCallback, object state) {
		return Dns.BeginGetHostAddresses(hostname, requestCallback, state);
	}

	public EndPoint[] EndGetHostEndpoints(IAsyncResult asyncResult) {
		var addresses = Dns.EndGetHostAddresses(asyncResult);

		return addresses.Select(address => new IPEndPoint(address, managerHttpPort).WithClusterDns(hostname)).ToArray();
	}
}
