// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using Google.Protobuf;

// ReSharper disable once CheckNamespace
namespace EventStore.Cluster;

partial class ReplicaLogWrite {
	public ReplicaLogWrite(long logPosition, byte[] replicaId) {
		LogPosition = logPosition;
		ReplicaId = ByteString.CopyFrom(replicaId);
	}
}
partial class ReplicatedTo {
	public ReplicatedTo(long logPosition) {
		LogPosition = logPosition;
	}
}

partial class Epoch {
	public Epoch(long epochPosition, int epochNumber, byte[] epochId) {
		EpochPosition = epochPosition;
		EpochNumber = epochNumber;
		EpochId = ByteString.CopyFrom(epochId);
	}
}
partial class SubscribeReplica {
	public SubscribeReplica(long logPosition, byte[] chunkId, Epoch[] lastEpochs, byte[] ip, int port,
		byte[] leaderId, byte[] subscriptionId, bool isPromotable, int version) {
		LogPosition = logPosition;
		ChunkId = ByteString.CopyFrom(chunkId);
		LastEpochs.AddRange(lastEpochs);

		Ip = ByteString.CopyFrom(ip);
		Port = port;
		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
		IsPromotable = isPromotable;
		Version = version;
	}
}

partial class ReplicaSubscriptionRetry {
	public ReplicaSubscriptionRetry(byte[] leaderId, byte[] subscriptionId) {
		Ensure.NotNull(leaderId, "leaderId");
		Ensure.NotNull(subscriptionId, "subscriptionId");

		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
	}
}
partial class ReplicaSubscribed {
	public ReplicaSubscribed(byte[] leaderId, byte[] subscriptionId, long subscriptionPosition) {
		Ensure.NotNull(leaderId, "leaderId");
		Ensure.NotNull(subscriptionId, "subscriptionId");
		Ensure.Nonnegative(subscriptionPosition, "subscriptionPosition");

		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
		SubscriptionPosition = subscriptionPosition;
	}
}

partial class ReplicaLogPositionAck {
	public ReplicaLogPositionAck(
		byte[] subscriptionId,
		long replicationLogPosition,
		long writerLogPosition) {

		SubscriptionId = ByteString.CopyFrom(subscriptionId);
		ReplicationLogPosition = replicationLogPosition;
		WriterLogPosition = writerLogPosition;
	}
}

partial class CreateChunk {
	public CreateChunk(byte[] leaderId, byte[] subscriptionId, byte[] chunkHeaderBytes, int fileSize,
		bool isScavengedChunk, ReadOnlySpan<byte> transformHeaderBytes) {
		Ensure.NotNull(leaderId, "leaderId");
		Ensure.NotNull(subscriptionId, "subscriptionId");
		Ensure.NotNull(chunkHeaderBytes, "chunkHeaderBytes");

		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
		ChunkHeaderBytes = ByteString.CopyFrom(chunkHeaderBytes);
		FileSize = fileSize;
		IsScavengedChunk = isScavengedChunk;
		TransformHeaderBytes = ByteString.CopyFrom(transformHeaderBytes);
	}
}

partial class RawChunkBulk {
	public RawChunkBulk(byte[] leaderId,
		byte[] subscriptionId,
		int chunkStartNumber,
		int chunkEndNumber,
		int rawPosition,
		byte[] rawBytes,
		bool completeChunk) {
		Ensure.NotNull(leaderId, "leaderId");
		Ensure.NotNull(subscriptionId, "subscriptionId");
		Ensure.NotNull(rawBytes, "rawBytes");
		Ensure.Positive(rawBytes.Length, "rawBytes.Length"); // we should never send empty array, NEVER

		LeaderId = ByteString.CopyFrom(leaderId);
		;
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
		ChunkStartNumber = chunkStartNumber;
		ChunkEndNumber = chunkEndNumber;
		RawPosition = rawPosition;
		RawBytes = ByteString.CopyFrom(rawBytes);
		CompleteChunk = completeChunk;
	}
}
partial class DataChunkBulk {

	public DataChunkBulk(byte[] leaderId,
		byte[] subscriptionId,
		int chunkStartNumber,
		int chunkEndNumber,
		long subscriptionPosition,
		byte[] dataBytes,
		bool completeChunk) {
		Ensure.NotNull(leaderId, "leaderId");
		Ensure.NotNull(subscriptionId, "subscriptionId");
		Ensure.NotNull(dataBytes, "rawBytes");
		Ensure.Nonnegative(dataBytes.Length,
			"dataBytes.Length"); // we CAN send empty dataBytes array here, unlike as with completed chunks

		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
		ChunkStartNumber = chunkStartNumber;
		ChunkEndNumber = chunkEndNumber;
		SubscriptionPosition = subscriptionPosition;
		DataBytes = ByteString.CopyFrom(dataBytes);
		CompleteChunk = completeChunk;
	}
}

partial class FollowerAssignment {
	public FollowerAssignment(byte[] leaderId, byte[] subscriptionId) {
		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
	}
}

partial class CloneAssignment {
	public CloneAssignment(byte[] leaderId, byte[] subscriptionId) {
		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
	}
}

partial class DropSubscription {
	public DropSubscription(byte[] leaderId, byte[] subscriptionId) {
		LeaderId = ByteString.CopyFrom(leaderId);
		SubscriptionId = ByteString.CopyFrom(subscriptionId);
	}
}
