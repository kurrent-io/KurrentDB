// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Client;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Grpc;

namespace EventStore.Core.Cluster;

public class ClusterInfo {
	private static readonly EndPointComparer Comparer = new EndPointComparer();

	public readonly MemberInfo[] Members;

	public ClusterInfo(params MemberInfo[] members) : this((IEnumerable<MemberInfo>)members) {
	}

	public ClusterInfo(IEnumerable<MemberInfo> members) {
		Members = members.Safe().OrderByDescending<MemberInfo, EndPoint>(x => x.HttpEndPoint, Comparer)
			.ToArray();
	}

	public ClusterInfo(ClusterInfoDto dto) {
		Members = dto.Members.Safe().Select(x => new MemberInfo(x))
			.OrderByDescending<MemberInfo, EndPoint>(x => x.HttpEndPoint, Comparer).ToArray();
	}

	public override string ToString() {
		return string.Join("\n", Members.Select(s => s.ToString()));
	}

	public bool HasChangedSince(ClusterInfo other) {
		if (ReferenceEquals(null, other)) return true;
		if (ReferenceEquals(this, other)) return false;

		if (other.Members.Length != Members.Length)
			return true;

		for (int i = 0; i < Members.Length; i++) {
			if (!Members[i].Equals(other.Members[i]))
				return true;
		}

		return false;
	}
	
	internal static ClusterInfo FromGrpcClusterInfo(EventStore.Cluster.ClusterInfo grpcCluster, string clusterDns) {
		var receivedMembers = Array.ConvertAll(grpcCluster.Members.ToArray(), x =>
			new MemberInfo(
				Uuid.FromDto(x.InstanceId).ToGuid(), x.TimeStamp.FromTicksSinceEpoch(), (VNodeState)x.State,
				x.IsAlive,
				!x.InternalTcpUsesTls ? new DnsEndPoint(x.InternalTcp.Address, (int)x.InternalTcp.Port).WithClusterDns(clusterDns) : null,
				x.InternalTcpUsesTls ? new DnsEndPoint(x.InternalTcp.Address, (int)x.InternalTcp.Port).WithClusterDns(clusterDns) : null,
				!x.ExternalTcpUsesTls && x.ExternalTcp != null
					? new DnsEndPoint(x.ExternalTcp.Address, (int)x.ExternalTcp.Port).WithClusterDns(clusterDns)
					: null,
				x.ExternalTcpUsesTls && x.ExternalTcp != null
					? new DnsEndPoint(x.ExternalTcp.Address, (int)x.ExternalTcp.Port).WithClusterDns(clusterDns)
					: null,
				new DnsEndPoint(x.HttpEndPoint.Address, (int)x.HttpEndPoint.Port).WithClusterDns(clusterDns),
				x.AdvertiseHostToClientAs, (int)x.AdvertiseHttpPortToClientAs, (int)x.AdvertiseTcpPortToClientAs,
				x.LastCommitPosition, x.WriterCheckpoint, x.ChaserCheckpoint,
				x.EpochPosition, x.EpochNumber, Uuid.FromDto(x.EpochId).ToGuid(), x.NodePriority,
				x.IsReadOnlyReplica, x.EsVersion == String.Empty ? null : x.EsVersion
			)).ToArray();
		return new ClusterInfo(receivedMembers);
	}

	internal static EventStore.Cluster.ClusterInfo ToGrpcClusterInfo(ClusterInfo cluster) {
		var members = Array.ConvertAll(cluster.Members, x => new EventStore.Cluster.MemberInfo {
			InstanceId = Uuid.FromGuid(x.InstanceId).ToDto(),
			TimeStamp = x.TimeStamp.ToTicksSinceEpoch(),
			State = (EventStore.Cluster.MemberInfo.Types.VNodeState)x.State,
			IsAlive = x.IsAlive,
			HttpEndPoint = new EventStore.Cluster.EndPoint(
				x.HttpEndPoint.GetHost(),
				(uint)x.HttpEndPoint.GetPort()),
			InternalTcp = x.InternalSecureTcpEndPoint != null ?
				new EventStore.Cluster.EndPoint(
					x.InternalSecureTcpEndPoint.GetHost(),
					(uint)x.InternalSecureTcpEndPoint.GetPort()) :
				new EventStore.Cluster.EndPoint(
				x.InternalTcpEndPoint.GetHost(),
				(uint)x.InternalTcpEndPoint.GetPort()),
			InternalTcpUsesTls = x.InternalSecureTcpEndPoint != null,
			ExternalTcp = x.ExternalSecureTcpEndPoint != null ?
				new EventStore.Cluster.EndPoint(
					x.ExternalSecureTcpEndPoint.GetHost(),
					(uint)x.ExternalSecureTcpEndPoint.GetPort()) :
				x.ExternalTcpEndPoint != null ?
				new EventStore.Cluster.EndPoint(
				x.ExternalTcpEndPoint.GetHost(),
				(uint)x.ExternalTcpEndPoint.GetPort()) : null,
			ExternalTcpUsesTls = x.ExternalSecureTcpEndPoint != null,
			LastCommitPosition = x.LastCommitPosition,
			WriterCheckpoint = x.WriterCheckpoint,
			ChaserCheckpoint = x.ChaserCheckpoint,
			EpochPosition = x.EpochPosition,
			EpochNumber = x.EpochNumber,
			EpochId = Uuid.FromGuid(x.EpochId).ToDto(),
			NodePriority = x.NodePriority,
			IsReadOnlyReplica = x.IsReadOnlyReplica,
			AdvertiseHostToClientAs = x.AdvertiseHostToClientAs ?? "",
			AdvertiseHttpPortToClientAs = (uint)x.AdvertiseHttpPortToClientAs,
			AdvertiseTcpPortToClientAs = (uint)x.AdvertiseTcpPortToClientAs,
			EsVersion = x.ESVersion ?? String.Empty
		}).ToArray();
		var info = new EventStore.Cluster.ClusterInfo();
		info.Members.AddRange(members);
		return info;
	}
}
