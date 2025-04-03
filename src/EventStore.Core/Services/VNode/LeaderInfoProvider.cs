// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EndPoint = System.Net.EndPoint;


namespace EventStore.Core.Services.VNode;


public class LeaderInfoProvider {
	private readonly GossipAdvertiseInfo _gossipInfo;
	private readonly Cluster.MemberInfo _leaderInfo;

	public LeaderInfoProvider(GossipAdvertiseInfo gossipInfo, Cluster.MemberInfo leaderInfo) {
		Ensure.NotNull(gossipInfo, "gossipInfo");
		
		_gossipInfo = gossipInfo;
		_leaderInfo = leaderInfo;
	}

	public (EndPoint AdvertisedTcpEndPoint, bool IsTcpEndPointSecure, EndPoint AdvertisedHttpEndPoint)
		GetLeaderInfoEndPoints(){

		var endpoints = _leaderInfo != null 
			? (TcpEndPoint: _leaderInfo.ExternalTcpEndPoint ?? _leaderInfo.ExternalSecureTcpEndPoint,
				IsTcpEndPointSecure: _leaderInfo.ExternalSecureTcpEndPoint != null,
				HttpEndPoint: _leaderInfo.HttpEndPoint,
				AdvertiseHost: _leaderInfo.AdvertiseHostToClientAs,
				AdvertiseHttpPort: _leaderInfo.AdvertiseHttpPortToClientAs,
				AdvertiseTcpPort: _leaderInfo.AdvertiseTcpPortToClientAs)
			: (TcpEndPoint: _gossipInfo.ExternalTcp ?? _gossipInfo.ExternalSecureTcp,
				IsTcpEndPointSecure: _gossipInfo.ExternalSecureTcp != null,
				HttpEndPoint: _gossipInfo.HttpEndPoint,
				AdvertiseHost: _gossipInfo.AdvertiseHostToClientAs,
				AdvertiseHttpPort: _gossipInfo.AdvertiseHttpPortToClientAs,
				AdvertiseTcpPort: _gossipInfo.AdvertiseTcpPortToClientAs);

		var advertisedTcpEndPoint = endpoints.TcpEndPoint == null
			? null
			: new DnsEndPoint(
				string.IsNullOrEmpty(endpoints.AdvertiseHost)
					? endpoints.TcpEndPoint.GetHost()
					: endpoints.AdvertiseHost,
				endpoints.AdvertiseTcpPort == 0 ? endpoints.TcpEndPoint.GetPort() : endpoints.AdvertiseTcpPort);
		var advertisedHttpEndPoint = new DnsEndPoint(
			string.IsNullOrEmpty(endpoints.AdvertiseHost)
				? endpoints.HttpEndPoint.GetHost()
				: endpoints.AdvertiseHost,
			endpoints.AdvertiseHttpPort == 0 ? endpoints.HttpEndPoint.GetPort() : endpoints.AdvertiseHttpPort);

		return (advertisedTcpEndPoint, endpoints.IsTcpEndPointSecure, advertisedHttpEndPoint);
	}
}
