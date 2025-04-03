// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Data;

namespace EventStore.Core.Cluster;

public class ClientClusterInfo {
	public ClientMemberInfo[] Members { get; set; }
	public string ServerIp { get; set; }
	public int ServerPort { get; set; }

	public ClientClusterInfo() {
	}

	public ClientClusterInfo(ClusterInfo clusterInfo, string serverIp, int serverPort) {
		Members = clusterInfo.Members.Select(x => new ClientMemberInfo(x)).ToArray();
		ServerIp = serverIp;
		ServerPort = serverPort;
	}

	public override string ToString() {
		return string.Format("Server: {0}:{1}, Members: [{2}]",
			ServerIp, ServerPort,
			Members != null ? string.Join(",", Members.Select(m => m.ToString())) : "null");
	}

	public class ClientMemberInfo {
		public Guid InstanceId { get; set; }

		public DateTime TimeStamp { get; set; }
		public VNodeState State { get; set; }
		public bool IsAlive { get; set; }

		public string InternalTcpIp { get; set; }
		public int InternalTcpPort { get; set; }
		public int InternalSecureTcpPort { get; set; }

		public string ExternalTcpIp { get; set; }
		public int ExternalTcpPort { get; set; }
		public int ExternalSecureTcpPort { get; set; }

		public string InternalHttpEndPointIp { get; set; }
		public int InternalHttpEndPointPort { get; set; }

		public string HttpEndPointIp { get; set; }
		public int HttpEndPointPort { get; set; }

		public long LastCommitPosition { get; set; }
		public long WriterCheckpoint { get; set; }
		public long ChaserCheckpoint { get; set; }

		public long EpochPosition { get; set; }
		public int EpochNumber { get; set; }
		public Guid EpochId { get; set; }

		public int NodePriority { get; set; }
		public bool IsReadOnlyReplica { get; set; }

		public string ESVersion { get; set; }

		public ClientMemberInfo() {
		}

		public ClientMemberInfo(MemberInfo member) {
			InstanceId = member.InstanceId;

			TimeStamp = member.TimeStamp;
			State = member.State;
			IsAlive = member.IsAlive;

			InternalTcpIp = member.InternalTcpEndPoint is null
				? member.InternalSecureTcpEndPoint.GetHost()
				: member.InternalTcpEndPoint.GetHost();
			InternalSecureTcpPort = member.InternalSecureTcpEndPoint?.GetPort() ?? 0;
			InternalTcpPort = member.InternalTcpEndPoint?.GetPort() ?? 0;

			InternalHttpEndPointIp = member.HttpEndPoint.GetHost();
			InternalHttpEndPointPort = member.HttpEndPoint.GetPort();

			HttpEndPointIp = string.IsNullOrEmpty(member.AdvertiseHostToClientAs)
				? member.HttpEndPoint.GetHost()
				: member.AdvertiseHostToClientAs;
			HttpEndPointPort = member.AdvertiseHttpPortToClientAs == 0
				? member.HttpEndPoint.GetPort()
				: member.AdvertiseHttpPortToClientAs;

			ExternalTcpIp = string.IsNullOrEmpty(member.AdvertiseHostToClientAs)
				? member.ExternalSecureTcpEndPoint?.GetHost() ??
				  member.ExternalTcpEndPoint?.GetHost() ?? member.HttpEndPoint.GetHost()
				: member.AdvertiseHostToClientAs;

			ExternalTcpPort = member.AdvertiseTcpPortToClientAs == 0
				? member.ExternalTcpEndPoint?.GetPort() ?? 0
				: member.AdvertiseTcpPortToClientAs;
			ExternalSecureTcpPort = member.AdvertiseTcpPortToClientAs == 0
				? member.ExternalSecureTcpEndPoint?.GetPort() ?? 0
				: member.AdvertiseTcpPortToClientAs;

			LastCommitPosition = member.LastCommitPosition;
			WriterCheckpoint = member.WriterCheckpoint;
			ChaserCheckpoint = member.ChaserCheckpoint;

			EpochPosition = member.EpochPosition;
			EpochNumber = member.EpochNumber;
			EpochId = member.EpochId;

			NodePriority = member.NodePriority;
			IsReadOnlyReplica = member.IsReadOnlyReplica;

			ESVersion = member.ESVersion;
		}

		public override string ToString() {
			return
				$"InstanceId: {InstanceId:B}, TimeStamp: {TimeStamp:yyyy-MM-dd HH:mm:ss.fff}, State: {State}, IsAlive: {IsAlive}, " +
				$"InternalTcpIp: {InternalTcpIp}, InternalTcpPort: {InternalTcpPort}, InternalSecureTcpPort: {InternalSecureTcpPort}, " +
				$"ExternalTcpIp: {ExternalTcpIp}, ExternalTcpPort: {ExternalTcpPort}, ExternalSecureTcpPort: {ExternalSecureTcpPort}, " +
				$"InternalHttpEndPointIp: {InternalHttpEndPointIp}, InternalHttpEndPointPort: {InternalHttpEndPointPort}, " +
				$"HttpEndPointIp: {HttpEndPointIp}, HttpEndPointPort: {HttpEndPointPort}, " +
				$"LastCommitPosition: {LastCommitPosition}, WriterCheckpoint: {WriterCheckpoint}, ChaserCheckpoint: {ChaserCheckpoint}, " +
				$"EpochPosition: {EpochPosition}, EpochNumber: {EpochNumber}, EpochId: {EpochId:B}, NodePriority: {NodePriority}, " +
				$"IsReadOnlyReplica: {IsReadOnlyReplica}, DBVersion: ${ESVersion}";
		}
	}
}

