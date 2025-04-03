// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Cluster;

public class MemberInfo : IEquatable<MemberInfo> {
	public readonly Guid InstanceId;

	public readonly DateTime TimeStamp;
	public readonly VNodeState State;
	public readonly bool IsAlive;

	public readonly EndPoint InternalTcpEndPoint;
	public readonly EndPoint InternalSecureTcpEndPoint;
	public readonly EndPoint ExternalTcpEndPoint;
	public readonly EndPoint ExternalSecureTcpEndPoint;
	public readonly EndPoint HttpEndPoint;
	public readonly string AdvertiseHostToClientAs;
	public readonly int AdvertiseHttpPortToClientAs;
	public readonly int AdvertiseTcpPortToClientAs;

	public readonly long LastCommitPosition;
	public readonly long WriterCheckpoint;
	public readonly long ChaserCheckpoint;
	public readonly long EpochPosition;
	public readonly int EpochNumber;
	public readonly Guid EpochId;

	public readonly int NodePriority;
	public readonly bool IsReadOnlyReplica;

	public readonly string ESVersion;
	
	public static MemberInfo ForManager(Guid instanceId, DateTime timeStamp, bool isAlive,
		EndPoint httpEndPoint, string esVersion = VersionInfo.UnknownVersion) {
		return new MemberInfo(instanceId, timeStamp, VNodeState.Manager, isAlive,
			httpEndPoint, null, httpEndPoint, null,
			httpEndPoint, null, 0, 0,
			-1, -1, -1, -1, -1, Guid.Empty, 0, false, esVersion);
	}

	public static MemberInfo ForVNode(Guid instanceId,
		DateTime timeStamp,
		VNodeState state,
		bool isAlive,
		EndPoint internalTcpEndPoint,
		EndPoint internalSecureTcpEndPoint,
		EndPoint externalTcpEndPoint,
		EndPoint externalSecureTcpEndPoint,
		EndPoint httpEndPoint,
		string advertiseHostToClientAs,
		int advertiseHttpPortToClientAs,
		int advertiseTcpPortToClientAs,
		long lastCommitPosition,
		long writerCheckpoint,
		long chaserCheckpoint,
		long epochPosition,
		int epochNumber,
		Guid epochId,
		int nodePriority,
		bool isReadOnlyReplica, string esVersion = VersionInfo.UnknownVersion) {
		if (state == VNodeState.Manager)
			throw new ArgumentException(string.Format("Wrong State for VNode: {0}", state), "state");
		return new MemberInfo(instanceId, timeStamp, state, isAlive,
			internalTcpEndPoint, internalSecureTcpEndPoint,
			externalTcpEndPoint, externalSecureTcpEndPoint,
			httpEndPoint, advertiseHostToClientAs, advertiseHttpPortToClientAs, advertiseTcpPortToClientAs,
			lastCommitPosition, writerCheckpoint, chaserCheckpoint,
			epochPosition, epochNumber, epochId, nodePriority, isReadOnlyReplica, esVersion);
	}
	
	public static MemberInfo Initial(Guid instanceId,
		DateTime timeStamp,
		VNodeState state,
		bool isAlive,
		EndPoint internalTcpEndPoint,
		EndPoint internalSecureTcpEndPoint,
		EndPoint externalTcpEndPoint,
		EndPoint externalSecureTcpEndPoint,
		EndPoint httpEndPoint,
		string advertiseHostToClientAs,
		int advertiseHttpPortToClientAs,
		int advertiseTcpPortToClientAs,
		int nodePriority,
		bool isReadOnlyReplica, string esVersion = VersionInfo.UnknownVersion) {
		if (state == VNodeState.Manager)
			throw new ArgumentException(string.Format("Wrong State for VNode: {0}", state), "state");
		return new MemberInfo(instanceId, timeStamp, state, isAlive,
			internalTcpEndPoint, internalSecureTcpEndPoint,
			externalTcpEndPoint, externalSecureTcpEndPoint,
			httpEndPoint, advertiseHostToClientAs, advertiseHttpPortToClientAs, advertiseTcpPortToClientAs,
			-1, -1, -1, -1, -1, Guid.Empty, nodePriority, isReadOnlyReplica, esVersion);
	}

	internal MemberInfo(Guid instanceId, DateTime timeStamp, VNodeState state, bool isAlive,
		EndPoint internalTcpEndPoint, EndPoint internalSecureTcpEndPoint,
		EndPoint externalTcpEndPoint, EndPoint externalSecureTcpEndPoint,
		EndPoint httpEndPoint, string advertiseHostToClientAs, int advertiseHttpPortToClientAs, int advertiseTcpPortToClientAs,
		long lastCommitPosition, long writerCheckpoint, long chaserCheckpoint,
		long epochPosition, int epochNumber, Guid epochId, int nodePriority, bool isReadOnlyReplica, string esVersion = null) {
		Ensure.Equal(false, internalTcpEndPoint == null && internalSecureTcpEndPoint == null, "Both internal TCP endpoints are null");
		Ensure.NotNull(httpEndPoint, nameof(httpEndPoint));

		InstanceId = instanceId;

		TimeStamp = timeStamp;
		State = state;
		IsAlive = isAlive;

		InternalTcpEndPoint = internalTcpEndPoint;
		InternalSecureTcpEndPoint = internalSecureTcpEndPoint;
		ExternalTcpEndPoint = externalTcpEndPoint;
		ExternalSecureTcpEndPoint = externalSecureTcpEndPoint;
		HttpEndPoint = httpEndPoint;
		AdvertiseHostToClientAs = advertiseHostToClientAs;
		AdvertiseHttpPortToClientAs = advertiseHttpPortToClientAs;
		AdvertiseTcpPortToClientAs = advertiseTcpPortToClientAs;

		LastCommitPosition = lastCommitPosition;
		WriterCheckpoint = writerCheckpoint;
		ChaserCheckpoint = chaserCheckpoint;

		EpochPosition = epochPosition;
		EpochNumber = epochNumber;
		EpochId = epochId;

		NodePriority = nodePriority;
		IsReadOnlyReplica = isReadOnlyReplica;

		ESVersion = esVersion;
	}

	internal MemberInfo(MemberInfoDto dto) {
		InstanceId = dto.InstanceId;
		TimeStamp = dto.TimeStamp;
		State = dto.State;
		IsAlive = dto.IsAlive;
		InternalTcpEndPoint = new DnsEndPoint(dto.InternalTcpIp, dto.InternalTcpPort);
		InternalSecureTcpEndPoint = dto.InternalSecureTcpPort > 0
			? new DnsEndPoint(dto.InternalTcpIp, dto.InternalSecureTcpPort)
			: null;
		ExternalTcpEndPoint = dto.ExternalTcpIp != null ? new DnsEndPoint(dto.ExternalTcpIp, dto.ExternalTcpPort) : null;
		ExternalSecureTcpEndPoint = dto.ExternalTcpIp != null && dto.ExternalSecureTcpPort > 0
			? new DnsEndPoint(dto.ExternalTcpIp, dto.ExternalSecureTcpPort)
			: null;
		HttpEndPoint = new DnsEndPoint(dto.HttpEndPointIp, dto.HttpEndPointPort);
		AdvertiseHostToClientAs = dto.AdvertiseHostToClientAs;
		AdvertiseHttpPortToClientAs = dto.AdvertiseHttpPortToClientAs;
		AdvertiseTcpPortToClientAs = dto.AdvertiseTcpPortToClientAs;
		LastCommitPosition = dto.LastCommitPosition;
		WriterCheckpoint = dto.WriterCheckpoint;
		ChaserCheckpoint = dto.ChaserCheckpoint;
		EpochPosition = dto.EpochPosition;
		EpochNumber = dto.EpochNumber;
		EpochId = dto.EpochId;
		NodePriority = dto.NodePriority;
		IsReadOnlyReplica = dto.IsReadOnlyReplica;
	}

	public bool Is(EndPoint endPoint) {
		return endPoint != null
		       && HttpEndPoint.EndPointEquals(endPoint)
		          || (InternalTcpEndPoint != null && InternalTcpEndPoint.EndPointEquals(endPoint))
		          || (InternalSecureTcpEndPoint != null && InternalSecureTcpEndPoint.EndPointEquals(endPoint))
		          || (ExternalTcpEndPoint != null && ExternalTcpEndPoint.EndPointEquals(endPoint))
		          || (ExternalSecureTcpEndPoint != null && ExternalSecureTcpEndPoint.EndPointEquals(endPoint));
	}
	
	public MemberInfo Updated(DateTime utcNow,
		VNodeState? state = null,
		bool? isAlive = null,
		long? lastCommitPosition = null,
		long? writerCheckpoint = null,
		long? chaserCheckpoint = null,
		EpochRecord epoch = null,
		int? nodePriority = null, string esVersion = null) {
		return new MemberInfo(InstanceId,
			utcNow,
			state ?? State,
			isAlive ?? IsAlive,
			InternalTcpEndPoint,
			InternalSecureTcpEndPoint,
			ExternalTcpEndPoint,
			ExternalSecureTcpEndPoint,
			HttpEndPoint,
			AdvertiseHostToClientAs,
			AdvertiseHttpPortToClientAs,
			AdvertiseTcpPortToClientAs,
			lastCommitPosition ?? LastCommitPosition,
			writerCheckpoint ?? WriterCheckpoint,
			chaserCheckpoint ?? ChaserCheckpoint,
			epoch != null ? epoch.EpochPosition : EpochPosition,
			epoch != null ? epoch.EpochNumber : EpochNumber,
			epoch != null ? epoch.EpochId : EpochId,
			nodePriority ?? NodePriority,
			IsReadOnlyReplica, esVersion ?? ESVersion);
	}

	public override string ToString() {
		if (State == VNodeState.Manager)
			return
				$"MAN {InstanceId:B} <{(IsAlive ? "LIVE" : "DEAD")}> [{State}, {HttpEndPoint}] | {TimeStamp:yyyy-MM-dd HH:mm:ss.fff}";
		return
			$"Priority: {NodePriority} VND {InstanceId:B} <{(IsAlive ? "LIVE" : "DEAD")}> [{State}, " +
			$"{(InternalTcpEndPoint == null ? "n/a" : InternalTcpEndPoint.ToString())}, " +
			$"{(InternalSecureTcpEndPoint == null ? "n/a" : InternalSecureTcpEndPoint.ToString())}, " +
			$"{(ExternalTcpEndPoint == null ? "n/a" : ExternalTcpEndPoint.ToString())}, " +
			$"{(ExternalSecureTcpEndPoint == null ? "n/a" : ExternalSecureTcpEndPoint.ToString())}, " +
			$"{HttpEndPoint}, (ADVERTISED: HTTP:{AdvertiseHostToClientAs}:{AdvertiseHttpPortToClientAs}, TCP:{AdvertiseHostToClientAs}:{AdvertiseTcpPortToClientAs}), " +
			$"Version: {ESVersion}] " + 
			$"{LastCommitPosition}/{WriterCheckpoint}/{ChaserCheckpoint}/E{EpochNumber}@{EpochPosition}:{EpochId:B} | {TimeStamp:yyyy-MM-dd HH:mm:ss.fff}";
	}

	public bool Equals(MemberInfo other) {
		// we ignore timestamp and checkpoints for equality comparison
		if (ReferenceEquals(null, other)) return false;
		if (ReferenceEquals(this, other)) return true;
		return other.InstanceId == InstanceId
		       && other.State == State
		       && other.IsAlive == IsAlive
		       && Equals(other.InternalTcpEndPoint, InternalTcpEndPoint)
		       && Equals(other.InternalSecureTcpEndPoint, InternalSecureTcpEndPoint)
		       && Equals(other.ExternalTcpEndPoint, ExternalTcpEndPoint)
		       && Equals(other.ExternalSecureTcpEndPoint, ExternalSecureTcpEndPoint)
		       && Equals(other.HttpEndPoint, HttpEndPoint)
		       && other.AdvertiseHostToClientAs == AdvertiseHostToClientAs
		       && other.AdvertiseHttpPortToClientAs == AdvertiseHttpPortToClientAs
		       && other.AdvertiseTcpPortToClientAs == AdvertiseTcpPortToClientAs
		       && other.EpochPosition == EpochPosition
		       && other.EpochNumber == EpochNumber
		       && other.EpochId == EpochId
		       && other.NodePriority == NodePriority
			   && other.IsReadOnlyReplica == IsReadOnlyReplica
		       && other.ESVersion == ESVersion;
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != typeof(MemberInfo)) return false;
		return Equals((MemberInfo)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int result = InstanceId.GetHashCode();
			result = (result * 397) ^ State.GetHashCode();
			result = (result * 397) ^ IsAlive.GetHashCode();
			result = (result * 397) ^ (InternalTcpEndPoint != null ? InternalTcpEndPoint.GetHashCode() : 0);
			result = (result * 397) ^
			         (InternalSecureTcpEndPoint != null ? InternalSecureTcpEndPoint.GetHashCode() : 0);
			result = (result * 397) ^ (ExternalTcpEndPoint != null ? ExternalTcpEndPoint.GetHashCode() : 0);
			result = (result * 397) ^
			         (ExternalSecureTcpEndPoint != null ? ExternalSecureTcpEndPoint.GetHashCode() : 0);
			result = (result * 397) ^ HttpEndPoint.GetHashCode();
			result = (result * 397) ^ (AdvertiseHostToClientAs != null ? AdvertiseHostToClientAs.GetHashCode() : 0);
			result = (result * 397) ^ AdvertiseHttpPortToClientAs.GetHashCode();
			result = (result * 397) ^ AdvertiseTcpPortToClientAs.GetHashCode();
			result = (result * 397) ^ EpochPosition.GetHashCode();
			result = (result * 397) ^ EpochNumber.GetHashCode();
			result = (result * 397) ^ EpochId.GetHashCode();
			result = (result * 397) ^ NodePriority;
			result = (result * 397) ^ (ESVersion != null ? ESVersion.GetHashCode() : 0);
			return result;
		}
	}
}
