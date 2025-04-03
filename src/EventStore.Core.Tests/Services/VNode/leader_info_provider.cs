// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Services.VNode;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode;

[TestFixture]
public class leader_info_provider {
	private const string DefaultHttpEndPoint = "9.9.9.9:9";

	public static IEnumerable<object[]> TestCases() {
		var leaderInfoCases = new TestCase[] {
			new ("Leader: NoEndPoints",
				Given: new (Leader: new()),
				Expected: new (TcpEndPoint: null)),
			new ("Leader: TcpEndPoint",
				Given: new (Leader: new(TcpEndPoint: "1.1.1.1:1")),
				Expected: new (TcpEndPoint: "1.1.1.1:1", IsTcpSecure: false)),
			new ("Leader: SecureTcpEndPoint",
				Given: new (Leader: new(SecureTcpEndPoint: "2.2.2.2:2")),
				Expected: new (TcpEndPoint: "2.2.2.2:2", IsTcpSecure: true)),
			new ("Leader: TcpEndPoint + SecureTcpEndPoint",
				Given: new (Leader: new(TcpEndPoint: "1.1.1.1:1", SecureTcpEndPoint: "2.2.2.2:2")),
				Expected: new (TcpEndPoint: "1.1.1.1:1", IsTcpSecure: true)),
			// AdvertiseTcpPort
			new ("Leader: AdvertiseTcpPort & NoEndPoints",
				Given: new (Leader: new(AdvertiseTcpPort: 9)),
				Expected: new (TcpEndPoint: null, IsTcpSecure: false)),
			new ("Leader: AdvertiseTcpPort & TcpEndPoint",
				Given: new (Leader: new(AdvertiseTcpPort: 9, TcpEndPoint: "1.1.1.1:1")),
				Expected: new (TcpEndPoint: "1.1.1.1:9")),
			new ("Leader: AdvertiseTcpPort & SecureTcpEndPoint",
				Given: new (Leader: new(AdvertiseTcpPort: 9, SecureTcpEndPoint: "2.2.2.2:2")),
				Expected: new (TcpEndPoint: "2.2.2.2:9", IsTcpSecure: true)),
			new ("Leader: AdvertiseTcpPort & TcpEndPoint + SecureTcpEndPoint",
				Given: new (Leader: new(TcpEndPoint: "1.1.1.1:1", SecureTcpEndPoint: "2.2.2.2:2", AdvertiseTcpPort: 9)),
				Expected: new (TcpEndPoint: "1.1.1.1:9", IsTcpSecure: true)),
			// AdvertiseHost
			new ("Leader: AdvertiseHost & NoEndPoints",
				Given: new (Leader: new(AdvertiseHost: "host", HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: null, IsTcpSecure: false, HttpEndPoint: "host:3")),
			new ("Leader: AdvertiseHost & TcpEndPoint",
				Given: new (Leader: new(AdvertiseHost: "host", TcpEndPoint: "1.1.1.1:1", HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: "host:1", HttpEndPoint: "host:3")),
			new ("Leader: AdvertiseHost & SecureTcpEndPoint",
				Given: new (Leader: new(AdvertiseHost: "host", SecureTcpEndPoint: "2.2.2.2:2", HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: "host:2", IsTcpSecure: true, HttpEndPoint: "host:3")),
			new ("Leader: AdvertiseHost & TcpEndPoint + SecureTcpEndPoint",
				Given: new (Leader: new(AdvertiseHost: "host", TcpEndPoint: "1.1.1.1:1", SecureTcpEndPoint: "2.2.2.2:2", HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: "host:1", IsTcpSecure: true, HttpEndPoint: "host:3")), //?? is this intended, secure with port of insecure
			// AdvertiseHost + AdvertisePort
			new ("Leader: AdvertiseHost + Port & NoEndPoints",
				Given: new (Leader: new(AdvertiseHost: "host", AdvertiseTcpPort: 9, HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: null, IsTcpSecure: false, HttpEndPoint: "host:3")),
			new ("Leader: AdvertiseHost + Port & TcpEndPoint",
				Given: new (Leader: new(AdvertiseHost: "host", AdvertiseTcpPort: 9, TcpEndPoint: "1.1.1.1:1",  HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: "host:9", IsTcpSecure: false, HttpEndPoint: "host:3")),
			new ("Leader: AdvertiseHost + Port & SecureTcpEndPoint",
				Given: new (Leader: new(AdvertiseHost: "host", AdvertiseTcpPort: 9, SecureTcpEndPoint: "2.2.2.2:2", HttpEndPoint: "3.3.3.3:3")),
				Expected: new (TcpEndPoint: "host:9", IsTcpSecure: true, HttpEndPoint: "host:3")),
			// AdvertiseHttpPort
			new ("Leader: AdvertiseHttpPort & NoEndPoints",
				Given: new (Leader: new(AdvertiseHttpPort: 8, HttpEndPoint: "3.3.3.3:3")),
				Expected: new (HttpEndPoint: "3.3.3.3:8")),
			new ("Leader: AdvertiseHttpPort & AdvertiseHost",
				Given: new (Leader: new(AdvertiseHttpPort: 8, AdvertiseHost: "host", HttpEndPoint: "3.3.3.3:3")),
				Expected: new (HttpEndPoint: "host:8")),
		};

		var nodeInfoCases = new TestCase[] {
			new ("Gossip: NoEndPoints",
				Given: new (Gossip: new()),
				Expected: new (TcpEndPoint: null)),
			new ("Gossip: TcpEndPoint",
				Given: new (Gossip: new(TcpEndPoint: "4.4.4.4:4")),
				Expected: new (TcpEndPoint: "4.4.4.4:4", IsTcpSecure: false)),
			new ("Gossip: SecureTcpEndPoint",
				Given: new (Gossip: new(SecureTcpEndPoint: "5.5.5.5:5")),
				Expected: new (TcpEndPoint: "5.5.5.5:5", IsTcpSecure: true)),
			// AnyIP SecureTcpEndPoint
			new ("Gossip: TcpEndPoint + SecureTcpEndPoint",
				Given: new (Gossip: new(TcpEndPoint: "4.4.4.4:4", SecureTcpEndPoint: "5.5.5.5:5")),
				Expected: new (TcpEndPoint: "4.4.4.4:4", IsTcpSecure: true)),
			// AnyIP & HttpEndPoint
			new ("Gossip: HttpEndPoint",
				Given: new (Gossip: new(HttpEndPoint: "8.8.8.8:8")),
				Expected: new (HttpEndPoint: "8.8.8.8:8")),
			new ("Gossip: HttpEndPoint & AdvertiseHost",
				Given: new (Gossip: new(HttpEndPoint: "8.8.8.8:8", AdvertiseHost: "host")),
				Expected: new (HttpEndPoint: "host:8")),
			new ("Gossip: HttpEndPoint & AdvertisePort",
				Given: new (Gossip: new(HttpEndPoint: "8.8.8.8:8", AdvertiseHttpPort: 9)),
				Expected: new (HttpEndPoint: "8.8.8.8:9")),
			new ("Gossip: HttpEndPoint & AdvertiseHost + Port",
				Given: new (Gossip: new(HttpEndPoint: "8.8.8.8:8", AdvertiseHost: "host", AdvertiseHttpPort: 9)),
				Expected: new (HttpEndPoint: "host:9")),
		};

		foreach (var testCase in leaderInfoCases.Concat(nodeInfoCases)) {
			yield return new object[] { testCase };
		}
	}

	[TestCaseSource(nameof(TestCases))]
	public void should_provide_as_expected(TestCase t) {

		var given = t.BuildGiven();
		var expected = t.BuildExpected();

		LeaderInfoProvider leaderInfoProvider = new LeaderInfoProvider(
			given.GossipInfo,
			given.LeaderInfo);

		var result = leaderInfoProvider.GetLeaderInfoEndPoints();

		AssertAreEqual(expected.TcpEndPoint, result.AdvertisedTcpEndPoint, "TcpEndPoint");
		AssertAreEqual(expected.HttpEndPoint, result.AdvertisedHttpEndPoint, "HttpEndPoint");
		Assert.AreEqual(expected.IsSecure, result.IsTcpEndPointSecure);
	}

	private void AssertAreEqual(EndPoint expected, EndPoint actual, string msg) {

		if (expected == null) {
			Assert.IsNull(actual);
			return;
		}

		Assert.AreEqual(expected.GetHost(), actual.GetHost(), $"{msg} host");
		Assert.AreEqual(expected.GetPort(), actual.GetPort(), $"{msg} port");
	}

	public record TestCase(string Test, GivenInput Given, ExpectedInput Expected) {
		public override string ToString() => Test;

		public Given BuildGiven() => Given.Build();
		public Expected BuildExpected() => Expected.Build();
	}

	public record Given(MemberInfo LeaderInfo, GossipAdvertiseInfo GossipInfo);

	public record Expected(EndPoint TcpEndPoint, bool IsSecure, EndPoint HttpEndPoint);

	public record LeaderInput(
		string TcpEndPoint=null,
		string SecureTcpEndPoint=null,
		string HttpEndPoint=null,
		int AdvertiseTcpPort=0,
		int AdvertiseHttpPort=0,
		string AdvertiseHost=null);

	public record GossipInput(
		string TcpEndPoint=null,
		string SecureTcpEndPoint=null,
		string HttpEndPoint=null,
		int AdvertiseTcpPort=0,
		int AdvertiseHttpPort=0,
		string AdvertiseHost=null);

	public record GivenInput(
		LeaderInput Leader = null,
		GossipInput Gossip = null) {

		public MemberInfo BuildLeaderInfo() {
			if (Leader == null) {
				return null;
			}

			return MemberInfo.Initial(
				instanceId: Guid.NewGuid(),
				timeStamp: DateTime.Now,
				state: VNodeState.Initializing,
				isAlive: false,
				internalTcpEndPoint: IPEndPoint.Parse("1.1.2.2:3"),
				internalSecureTcpEndPoint: IPEndPoint.Parse("4.4.5.5:6"),
				externalTcpEndPoint: Leader.TcpEndPoint == null ? null : IPEndPoint.Parse(Leader.TcpEndPoint),
				externalSecureTcpEndPoint: Leader.SecureTcpEndPoint == null ? null : IPEndPoint.Parse(Leader.SecureTcpEndPoint),
				httpEndPoint: IPEndPoint.Parse(Leader.HttpEndPoint ?? DefaultHttpEndPoint), 
				advertiseHostToClientAs: Leader.AdvertiseHost,
				advertiseHttpPortToClientAs: Leader.AdvertiseHttpPort,
				advertiseTcpPortToClientAs: Leader.AdvertiseTcpPort,
				nodePriority: 1,
				isReadOnlyReplica: false);
		}

		public GossipAdvertiseInfo BuildGossipInfo() {
			return new GossipAdvertiseInfo(
				externalTcp: ParseDnsEndPoint(Gossip?.TcpEndPoint),
				externalSecureTcp: ParseDnsEndPoint(Gossip?.SecureTcpEndPoint),
				httpEndPoint: ParseDnsEndPoint(Gossip?.HttpEndPoint ?? DefaultHttpEndPoint),
				advertiseHostToClientAs: Gossip?.AdvertiseHost,
				advertiseHttpPortToClientAs: Gossip?.AdvertiseHttpPort ?? 8,
				advertiseTcpPortToClientAs: Gossip?.AdvertiseTcpPort ?? 9,
				internalTcp: new DnsEndPoint("internal.tcp", 1),
				internalSecureTcp: new DnsEndPoint("secure.tcp", 2),
				advertiseInternalHostAs: null,
                advertiseExternalHostAs: null,
                advertiseHttpPortAs: 0);
		}

		public Given Build() {
			return new Given(
				BuildLeaderInfo(),
				BuildGossipInfo());
		}
	}

	public record ExpectedInput(string TcpEndPoint=null, string HttpEndPoint=null, bool IsTcpSecure=false) {
		public Expected Build() {
			return new Expected(
				ParseEndPoint(TcpEndPoint),
				IsTcpSecure,
				ParseEndPoint(HttpEndPoint ?? DefaultHttpEndPoint));
		}
	}

	private static EndPoint ParseEndPoint(string s) {
		if (s == null) {
			return null;
		}

		if (IPEndPoint.TryParse(s, out var ipEndPoint)) {
			return ipEndPoint;
		}

		return ParseDnsEndPoint(s);
	}

	private static DnsEndPoint ParseDnsEndPoint(string s) {
		if (s == null) {
			return null;
		}

		var parts = s.Split(":");
		var host = parts[0];
		var port = int.Parse(parts[1]);

		return new DnsEndPoint(host, port);
	}
}
