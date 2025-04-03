// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Data;
using NUnit.Framework;

namespace EventStore.Core.Tests.Cluster;

[TestFixture]
public class MemberInfoTests {
	[Test]
	public void member_with_dns_endpoint_should_equal() {
		var ipAddress = "127.0.0.1";
		var port = 1113;
		var memberWithDnsEndPoint = EventStore.Core.Cluster.MemberInfo.Initial(Guid.Empty, DateTime.UtcNow,
			VNodeState.Unknown, true,
			new DnsEndPoint(ipAddress, port),
			new DnsEndPoint(ipAddress, port),
			new DnsEndPoint(ipAddress, port),
			new DnsEndPoint(ipAddress, port),
			new DnsEndPoint(ipAddress, port),
			null, 0, 0,
			0, false);

		var ipEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
		var dnsEndPoint = new DnsEndPoint(ipAddress, port);

		Assert.True(memberWithDnsEndPoint.Is(ipEndPoint));
		Assert.True(memberWithDnsEndPoint.Is(dnsEndPoint));
	}

	[Test]
	public void member_with_ip_endpoint_should_equal() {
		var ipAddress = "127.0.0.1";
		var port = 1113;
		var memberWithDnsEndPoint = EventStore.Core.Cluster.MemberInfo.Initial(Guid.Empty, DateTime.UtcNow,
			VNodeState.Unknown, true,
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			new IPEndPoint(IPAddress.Parse(ipAddress), port),
			null, 0, 0, 0, false);

		var ipEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
		var dnsEndPoint = new DnsEndPoint(ipAddress, port);

		Assert.True(memberWithDnsEndPoint.Is(ipEndPoint));
		Assert.True(memberWithDnsEndPoint.Is(dnsEndPoint));
	}
}
