// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using EventStore.Cluster;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Services.Transport.Tcp;

public class InternalTcpDispatcher : ClientWriteTcpDispatcher {
	public InternalTcpDispatcher(TimeSpan writeTimeout) : base(writeTimeout) {
		AddUnwrapper(TcpCommand.LeaderReplicatedTo, UnwrapReplicatedTo, ClientVersion.V2);
		AddWrapper<ReplicationTrackingMessage.ReplicatedTo>(WrapReplicatedTo, ClientVersion.V2);

		AddUnwrapper(TcpCommand.SubscribeReplica, UnwrapReplicaSubscriptionRequest, ClientVersion.V2);
		AddWrapper<ReplicationMessage.SubscribeReplica>(WrapSubscribeReplica, ClientVersion.V2);
		AddUnwrapper(TcpCommand.ReplicaLogPositionAck, UnwrapReplicaLogPositionAck, ClientVersion.V2);
		AddWrapper<ReplicationMessage.AckLogPosition>(WrapAckLogPosition, ClientVersion.V2);
		AddUnwrapper(TcpCommand.CreateChunk, UnwrapCreateChunk, ClientVersion.V2);
		AddWrapper<ReplicationMessage.CreateChunk>(WrapCreateChunk, ClientVersion.V2);
		AddUnwrapper(TcpCommand.RawChunkBulk, UnwrapRawChunkBulk, ClientVersion.V2);
		AddWrapper<ReplicationMessage.RawChunkBulk>(WrapRawChunkBulk, ClientVersion.V2);
		AddUnwrapper(TcpCommand.DataChunkBulk, UnwrapDataChunkBulk, ClientVersion.V2);
		AddWrapper<ReplicationMessage.DataChunkBulk>(WrapDataChunkBulk, ClientVersion.V2);
		AddUnwrapper(TcpCommand.ReplicaSubscriptionRetry, UnwrapReplicaSubscriptionRetry, ClientVersion.V2);
		AddWrapper<ReplicationMessage.ReplicaSubscriptionRetry>(WrapReplicaSubscriptionRetry, ClientVersion.V2);
		AddUnwrapper(TcpCommand.ReplicaSubscribed, UnwrapReplicaSubscribed, ClientVersion.V2);
		AddWrapper<ReplicationMessage.ReplicaSubscribed>(WrapReplicaSubscribed, ClientVersion.V2);

		AddUnwrapper(TcpCommand.FollowerAssignment, UnwrapFollowerAssignment, ClientVersion.V2);
		AddWrapper<ReplicationMessage.FollowerAssignment>(WrapFollowerAssignment, ClientVersion.V2);
		AddUnwrapper(TcpCommand.CloneAssignment, UnwrapCloneAssignment, ClientVersion.V2);
		AddWrapper<ReplicationMessage.CloneAssignment>(WrapCloneAssignment, ClientVersion.V2);
		AddUnwrapper(TcpCommand.DropSubscription, UnwrapDropSubscription, ClientVersion.V2);
		AddWrapper<ReplicationMessage.DropSubscription>(WrapDropSubscription, ClientVersion.V2);
	}

	private static TcpPackage WrapReplicatedTo(ReplicationTrackingMessage.ReplicatedTo msg) {
		var dto = new ReplicatedTo(msg.LogPosition);
		return new(TcpCommand.LeaderReplicatedTo, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationTrackingMessage.LeaderReplicatedTo UnwrapReplicatedTo(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<ReplicatedTo>();
		return new(dto.LogPosition);
	}

	private static ReplicationMessage.ReplicaSubscriptionRequest UnwrapReplicaSubscriptionRequest(TcpPackage package, IEnvelope envelope, TcpConnectionManager connection) {
		var dto = package.Data.Deserialize<SubscribeReplica>();
		var vnodeTcpEndPoint = new DnsEndPoint(Helper.UTF8NoBom.GetString(dto.Ip.Span), dto.Port);
		var lastEpochs = dto.LastEpochs.Safe().Select(x => new Core.Data.Epoch(x.EpochPosition, x.EpochNumber, new Guid(x.EpochId.Span))).ToArray();
		return new(package.CorrelationId,
			envelope,
			connection,
			dto.Version,
			dto.LogPosition,
			new Guid(dto.ChunkId.Span),
			lastEpochs,
			vnodeTcpEndPoint,
			new Guid(dto.LeaderId.Span),
			new Guid(dto.SubscriptionId.Span),
			dto.IsPromotable);
	}

	private static TcpPackage WrapSubscribeReplica(ReplicationMessage.SubscribeReplica msg) {
		var epochs = msg.LastEpochs.Select(x => new Epoch(x.EpochPosition, x.EpochNumber, x.EpochId.ToByteArray())).ToArray();
		var dto = new SubscribeReplica(msg.LogPosition,
			msg.ChunkId.ToByteArray(),
			epochs,
			Helper.UTF8NoBom.GetBytes(msg.ReplicaEndPoint.GetHost()),
			msg.ReplicaEndPoint.GetPort(),
			msg.LeaderId.ToByteArray(),
			msg.SubscriptionId.ToByteArray(),
			msg.IsPromotable,
			msg.Version);
		return new(TcpCommand.SubscribeReplica, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.ReplicaLogPositionAck UnwrapReplicaLogPositionAck(TcpPackage package, IEnvelope envelope, TcpConnectionManager connection) {
		var dto = package.Data.Deserialize<ReplicaLogPositionAck>();
		return new(new Guid(dto.SubscriptionId.Span), dto.ReplicationLogPosition, dto.WriterLogPosition);
	}

	private static TcpPackage WrapAckLogPosition(ReplicationMessage.AckLogPosition msg) {
		var dto = new ReplicaLogPositionAck(msg.SubscriptionId.ToByteArray(), msg.ReplicationLogPosition, msg.WriterLogPosition);
		return new(TcpCommand.ReplicaLogPositionAck, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.CreateChunk UnwrapCreateChunk(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<CreateChunk>();
		ChunkHeader chunkHeader = new(dto.ChunkHeaderBytes.Span);

		return new(new(dto.LeaderId.Span), new(dto.SubscriptionId.Span), chunkHeader, dto.FileSize, dto.IsScavengedChunk, dto.TransformHeaderBytes.Memory);
	}

	private static TcpPackage WrapCreateChunk(ReplicationMessage.CreateChunk msg) {
		var dto = new CreateChunk(msg.LeaderId.ToByteArray(),
			msg.SubscriptionId.ToByteArray(),
			msg.ChunkHeader.AsByteArray(),
			msg.FileSize,
			msg.IsScavengedChunk,
			msg.TransformHeader.Span);
		return new(TcpCommand.CreateChunk, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.RawChunkBulk UnwrapRawChunkBulk(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<RawChunkBulk>();
		return new(new Guid(dto.LeaderId.Span),
			new Guid(dto.SubscriptionId.Span),
			dto.ChunkStartNumber,
			dto.ChunkEndNumber,
			dto.RawPosition,
			dto.RawBytes.ToByteArray(),
			dto.CompleteChunk);
	}

	private static TcpPackage WrapRawChunkBulk(ReplicationMessage.RawChunkBulk msg) {
		var dto = new RawChunkBulk(msg.LeaderId.ToByteArray(),
			msg.SubscriptionId.ToByteArray(),
			msg.ChunkStartNumber,
			msg.ChunkEndNumber,
			msg.RawPosition,
			msg.RawBytes,
			msg.CompleteChunk);
		return new(TcpCommand.RawChunkBulk, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.DataChunkBulk UnwrapDataChunkBulk(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<DataChunkBulk>();
		return new(new Guid(dto.LeaderId.Span),
			new Guid(dto.SubscriptionId.Span),
			dto.ChunkStartNumber,
			dto.ChunkEndNumber,
			dto.SubscriptionPosition,
			dto.DataBytes.ToByteArray(),
			dto.CompleteChunk);
	}

	private static TcpPackage WrapDataChunkBulk(ReplicationMessage.DataChunkBulk msg) {
		var dto = new DataChunkBulk(msg.LeaderId.ToByteArray(),
			msg.SubscriptionId.ToByteArray(),
			msg.ChunkStartNumber,
			msg.ChunkEndNumber,
			msg.SubscriptionPosition,
			msg.DataBytes,
			msg.CompleteChunk);
		return new(TcpCommand.DataChunkBulk, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.ReplicaSubscriptionRetry UnwrapReplicaSubscriptionRetry(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<ReplicaSubscriptionRetry>();
		return new(new Guid(dto.LeaderId.Span), new Guid(dto.SubscriptionId.Span));
	}

	private static TcpPackage WrapReplicaSubscriptionRetry(ReplicationMessage.ReplicaSubscriptionRetry msg) {
		var dto = new ReplicaSubscriptionRetry(msg.LeaderId.ToByteArray(), msg.SubscriptionId.ToByteArray());
		return new(TcpCommand.ReplicaSubscriptionRetry, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.ReplicaSubscribed UnwrapReplicaSubscribed(TcpPackage package, IEnvelope envelope, TcpConnectionManager connection) {
		var dto = package.Data.Deserialize<ReplicaSubscribed>();
		return new(new Guid(dto.LeaderId.Span),
			new Guid(dto.SubscriptionId.Span),
			dto.SubscriptionPosition,
			connection.RemoteEndPoint);
	}

	private static TcpPackage WrapReplicaSubscribed(ReplicationMessage.ReplicaSubscribed msg) {
		var dto = new ReplicaSubscribed(msg.LeaderId.ToByteArray(),
			msg.SubscriptionId.ToByteArray(),
			msg.SubscriptionPosition);
		return new(TcpCommand.ReplicaSubscribed, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.FollowerAssignment UnwrapFollowerAssignment(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<FollowerAssignment>();
		return new(new Guid(dto.LeaderId.Span), new Guid(dto.SubscriptionId.Span));
	}

	private static TcpPackage WrapFollowerAssignment(ReplicationMessage.FollowerAssignment msg) {
		var dto = new FollowerAssignment(msg.LeaderId.ToByteArray(), msg.SubscriptionId.ToByteArray());
		return new(TcpCommand.FollowerAssignment, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.CloneAssignment UnwrapCloneAssignment(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<CloneAssignment>();
		return new(new Guid(dto.LeaderId.Span), new Guid(dto.SubscriptionId.Span));
	}

	private static TcpPackage WrapCloneAssignment(ReplicationMessage.CloneAssignment msg) {
		var dto = new CloneAssignment(msg.LeaderId.ToByteArray(), msg.SubscriptionId.ToByteArray());
		return new(TcpCommand.CloneAssignment, Guid.NewGuid(), dto.Serialize());
	}

	private static ReplicationMessage.DropSubscription UnwrapDropSubscription(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<CloneAssignment>();
		return new(new Guid(dto.LeaderId.Span), new Guid(dto.SubscriptionId.Span));
	}

	private static TcpPackage WrapDropSubscription(ReplicationMessage.DropSubscription msg) {
		var dto = new DropSubscription(msg.LeaderId.ToByteArray(), msg.SubscriptionId.ToByteArray());
		return new(TcpCommand.DropSubscription, Guid.NewGuid(), dto.Serialize());
	}
}
