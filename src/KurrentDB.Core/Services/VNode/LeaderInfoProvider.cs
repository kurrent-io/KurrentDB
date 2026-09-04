// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;
using EndPoint = System.Net.EndPoint;

namespace KurrentDB.Core.Services.VNode;


public class LeaderInfoProvider {
	private readonly GossipAdvertiseInfo _gossipInfo;
	private readonly MemberInfoLite _leaderInfo;
	private readonly Guid _nodeInstanceId;

	public LeaderInfoProvider(GossipAdvertiseInfo gossipInfo, MemberInfoLite leaderInfo, Guid nodeInstanceId) {
		Ensure.NotNull(gossipInfo, "gossipInfo");
		_gossipInfo = gossipInfo;
		_leaderInfo = leaderInfo;
		_nodeInstanceId = nodeInstanceId;
	}

	// Get the endpoints for a client to talk to the leader.
	// If we have leader info this is best. Otherwise go with what we heard on the grape vine.
	public (EndPoint AdvertisedTcpEndPoint, bool IsTcpEndPointSecure, EndPoint AdvertisedHttpEndPoint, Guid InstanceId)
		GetLeaderInfoEndPoints() {

		if (_leaderInfo is { } leader)
			return (
				AdvertisedTcpEndPoint: leader.ClientTcpEndPoint,
				IsTcpEndPointSecure: leader.ClientTcpApiIsSecure,
				AdvertisedHttpEndPoint: leader.ClientHttpEndPoint,
				InstanceId: leader.InstanceId);

		// TC: if we don't know who the leader is we return our own info. is this ideal?
		return (
			AdvertisedTcpEndPoint: MemberInfoLite.ToClientEndPoint(
				endPoint: _gossipInfo.ExternalTcp ?? _gossipInfo.ExternalSecureTcp,
				advertiseHost: _gossipInfo.AdvertiseHostToClientAs,
				advertisePort: _gossipInfo.AdvertiseTcpPortToClientAs),
			IsTcpEndPointSecure: _gossipInfo.ExternalSecureTcp != null,
			AdvertisedHttpEndPoint: MemberInfoLite.ToClientEndPoint(
				endPoint: _gossipInfo.HttpEndPoint,
				advertiseHost: _gossipInfo.AdvertiseHostToClientAs,
				advertisePort: _gossipInfo.AdvertiseHttpPortToClientAs),
			InstanceId: _nodeInstanceId);
	}
}
