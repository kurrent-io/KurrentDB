// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EndPoint = System.Net.EndPoint;

namespace EventStore.Core.Messages;

public static partial class ReplicationMessage {
	[DerivedMessage(CoreMessage.Replication)]
	public partial class SubscribeReplica : Message {
		public readonly int Version;
		public readonly long LogPosition;
		public readonly Guid ChunkId;
		public readonly IReadOnlyList<EpochRecord> LastEpochs;
		public readonly EndPoint ReplicaEndPoint;
		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;
		public readonly bool IsPromotable;

		public SubscribeReplica(
			int version,
			long logPosition, Guid chunkId, IReadOnlyList<EpochRecord> lastEpochs,
			EndPoint replicaEndPoint,
			Guid leaderId, Guid subscriptionId, bool isPromotable) {

			Ensure.Nonnegative(version, "version");
			Ensure.Nonnegative(logPosition, "logPosition");
			Ensure.NotNull(lastEpochs, "lastEpochs");
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			Ensure.NotNull(replicaEndPoint, "replicaEndPoint");

			Version = version;
			LogPosition = logPosition;
			ChunkId = chunkId;
			LastEpochs = lastEpochs;
			ReplicaEndPoint = replicaEndPoint;
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			IsPromotable = isPromotable;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class AckLogPosition : Message {
		public readonly Guid SubscriptionId;

		// where the replication subscription is up to.
		// used for managing the subscription
		public readonly long ReplicationLogPosition;

		// where we are up to writing data to the log.
		// can be behind ReplicationLogPosition.
		// used for determining what has been replicated
		public readonly long WriterLogPosition;

		public AckLogPosition(
			Guid subscriptionId,
			long replicationLogPosition,
			long writerLogPosition) {

			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			SubscriptionId = subscriptionId;
			ReplicationLogPosition = replicationLogPosition;
			WriterLogPosition = writerLogPosition;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class ReplicaLogPositionAck : Message {
		public readonly Guid SubscriptionId;
		public readonly long ReplicationLogPosition;
		public readonly long WriterLogPosition;

		public ReplicaLogPositionAck(
			Guid subscriptionId,
			long replicationLogPosition,
			long writerLogPosition) {

			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");

			SubscriptionId = subscriptionId;
			ReplicationLogPosition = replicationLogPosition;
			WriterLogPosition = writerLogPosition;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class ReplicaSubscriptionRequest : Message {
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;
		public readonly TcpConnectionManager Connection;

		public readonly int Version;
		public readonly long LogPosition;
		public readonly Guid ChunkId;
		public readonly Epoch[] LastEpochs;
		public readonly EndPoint ReplicaEndPoint;
		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;
		public readonly bool IsPromotable;

		public ReplicaSubscriptionRequest(Guid correlationId,
			IEnvelope envelope,
			TcpConnectionManager connection,
			int version,
			long logPosition,
			Guid chunkId,
			Epoch[] lastEpochs,
			EndPoint replicaEndPoint,
			Guid leaderId,
			Guid subscriptionId,
			bool isPromotable) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.NotNull(envelope, "envelope");
			Ensure.NotNull(connection, "connection");
			Ensure.Nonnegative(version, "version");
			Ensure.Nonnegative(logPosition, "logPosition");
			Ensure.NotNull(lastEpochs, "lastEpochs");
			Ensure.NotNull(replicaEndPoint, "ReplicaEndPoint");
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");

			Version = version;
			CorrelationId = correlationId;
			Envelope = envelope;
			Connection = connection;
			LogPosition = logPosition;
			ChunkId = chunkId;
			LastEpochs = lastEpochs;
			ReplicaEndPoint = replicaEndPoint;
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			IsPromotable = isPromotable;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class ReconnectToLeader : Message {
		public readonly MemberInfo Leader;
		public readonly Guid ConnectionCorrelationId;

		public ReconnectToLeader(Guid connectionCorrelationId, MemberInfo leader) {
			Ensure.NotEmptyGuid(connectionCorrelationId, nameof(connectionCorrelationId));
			Ensure.NotNull(leader, nameof(leader));
			ConnectionCorrelationId = connectionCorrelationId;
			Leader = leader;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class LeaderConnectionFailed : Message {
		public readonly MemberInfo Leader;
		public readonly Guid LeaderConnectionCorrelationId;

		public LeaderConnectionFailed(Guid leaderConnectionCorrelationId, MemberInfo leader) {
			Ensure.NotEmptyGuid(leaderConnectionCorrelationId, nameof(leaderConnectionCorrelationId));
			Ensure.NotNull(leader, nameof(leader));
			LeaderConnectionCorrelationId = leaderConnectionCorrelationId;
			Leader = leader;
		}
	}

	public interface IReplicationMessage {
		Guid LeaderId { get; }
		Guid SubscriptionId { get; }
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class SubscribeToLeader : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid StateCorrelationId;
		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public SubscribeToLeader(Guid stateCorrelationId, Guid leaderId, Guid subscriptionId) {
			Ensure.NotEmptyGuid(stateCorrelationId, "stateCorrelationId");
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");

			StateCorrelationId = stateCorrelationId;
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class ReplicaSubscriptionRetry : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public ReplicaSubscriptionRetry(Guid leaderId, Guid subscriptionId) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class ReplicaSubscribed : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;
		public readonly long SubscriptionPosition;

		public readonly EndPoint LeaderEndPoint;

		public ReplicaSubscribed(Guid leaderId, Guid subscriptionId, long subscriptionPosition) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			Ensure.Nonnegative(subscriptionPosition, "subscriptionPosition");

			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			SubscriptionPosition = subscriptionPosition;
		}

		public ReplicaSubscribed(Guid leaderId, Guid subscriptionId, long subscriptionPosition,
			EndPoint leaderEndPoint) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			Ensure.Nonnegative(subscriptionPosition, "subscriptionPosition");
			Ensure.NotNull(leaderEndPoint, "leaderEndPoint");

			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			SubscriptionPosition = subscriptionPosition;
			LeaderEndPoint = leaderEndPoint;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class FollowerAssignment : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public FollowerAssignment(Guid leaderId, Guid subscriptionId) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class CloneAssignment : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public CloneAssignment(Guid leaderId, Guid subscriptionId) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class CreateChunk : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public readonly ChunkHeader ChunkHeader;
		public readonly int FileSize;
		public bool IsScavengedChunk;

		public ReadOnlyMemory<byte> TransformHeader;

		public CreateChunk(Guid leaderId, Guid subscriptionId, ChunkHeader chunkHeader, int fileSize,
			bool isScavengedChunk, ReadOnlyMemory<byte> transformHeader) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			Ensure.NotNull(chunkHeader, "chunkHeader");

			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			ChunkHeader = chunkHeader;
			FileSize = fileSize;
			IsScavengedChunk = isScavengedChunk;
			TransformHeader = transformHeader;
		}

		public override string ToString() {
			return string.Format(
				"CreateChunk message: LeaderId: {0}, SubscriptionId: {1}, ChunkHeader: {2}, FileSize: {3}, IsScavengedChunk: {4}, TransformHeader size: {5}",
				LeaderId, SubscriptionId, ChunkHeader, FileSize, IsScavengedChunk, TransformHeader.Length);
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class RawChunkBulk : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public readonly int ChunkStartNumber;
		public readonly int ChunkEndNumber;
		public readonly int RawPosition;
		public readonly byte[] RawBytes;
		public readonly bool CompleteChunk;

		public RawChunkBulk(Guid leaderId,
			Guid subscriptionId,
			int chunkStartNumber, int chunkEndNumber,
			int rawPosition, byte[] rawBytes,
			bool completeChunk) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			Ensure.NotNull(rawBytes, "rawBytes");
			Ensure.Positive(rawBytes.Length, "rawBytes.Length"); // we should never send empty array, NEVER

			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			ChunkStartNumber = chunkStartNumber;
			ChunkEndNumber = chunkEndNumber;
			RawPosition = rawPosition;
			RawBytes = rawBytes;
			CompleteChunk = completeChunk;
		}

		public override string ToString() {
			return string.Format(
				"RawChunkBulk message: LeaderId: {0}, SubscriptionId: {1}, ChunkStartNumber: {2}, ChunkEndNumber: {3}, RawPosition: {4}, RawBytes length: {5}, CompleteChunk: {6}",
				LeaderId, SubscriptionId,
				ChunkStartNumber, ChunkEndNumber, RawPosition, RawBytes.Length, CompleteChunk);
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class DataChunkBulk : Message, IReplicationMessage, StorageMessage.IFlushableMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public readonly int ChunkStartNumber;
		public readonly int ChunkEndNumber;
		public readonly long SubscriptionPosition;
		public readonly byte[] DataBytes;
		public readonly bool CompleteChunk;

		public DataChunkBulk(Guid leaderId,
			Guid subscriptionId,
			int chunkStartNumber,
			int chunkEndNumber,
			long subscriptionPosition,
			byte[] dataBytes,
			bool completeChunk) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			Ensure.NotNull(dataBytes, "rawBytes");
			Ensure.Nonnegative(dataBytes.Length,
				"dataBytes.Length"); // we CAN send empty dataBytes array here, unlike as with completed chunks

			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
			ChunkStartNumber = chunkStartNumber;
			ChunkEndNumber = chunkEndNumber;
			SubscriptionPosition = subscriptionPosition;
			DataBytes = dataBytes;
			CompleteChunk = completeChunk;
		}

		public override string ToString() {
			return string.Format(
				"DataChunkBulk message: LeaderId: {0}, SubscriptionId: {1}, ChunkStartNumber: {2}, ChunkEndNumber: {3}, SubscriptionPosition: {4}, DataBytes length: {5}, CompleteChunk: {6}",
				LeaderId, SubscriptionId, ChunkStartNumber, ChunkEndNumber,
				SubscriptionPosition, DataBytes.Length, CompleteChunk);
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class DropSubscription : Message, IReplicationMessage {
		Guid IReplicationMessage.LeaderId {
			get { return LeaderId; }
		}

		Guid IReplicationMessage.SubscriptionId {
			get { return SubscriptionId; }
		}

		public readonly Guid LeaderId;
		public readonly Guid SubscriptionId;

		public DropSubscription(Guid leaderId, Guid subscriptionId) {
			Ensure.NotEmptyGuid(leaderId, "leaderId");
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			LeaderId = leaderId;
			SubscriptionId = subscriptionId;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class GetReplicationStats : Message {
		public IEnvelope Envelope;

		public GetReplicationStats(IEnvelope envelope) {
			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Replication)]
	public partial class GetReplicationStatsCompleted : Message {
		public List<ReplicationMessage.ReplicationStats> ReplicationStats;

		public GetReplicationStatsCompleted(List<ReplicationMessage.ReplicationStats> replicationStats) {
			ReplicationStats = replicationStats;
		}
	}

	public class ReplicationStats {
		public Guid SubscriptionId { get; private set; }
		public Guid ConnectionId { get; private set; }
		public string SubscriptionEndpoint { get; private set; }
		public long TotalBytesSent { get; private set; }
		public long TotalBytesReceived { get; private set; }
		public int PendingSendBytes { get; private set; }
		public int PendingReceivedBytes { get; private set; }
		public int SendQueueSize { get; private set; }

		public ReplicationStats(Guid subscriptionId, Guid connectionId, string subscriptionEndpoint,
			int sendQueueSize,
			long totalBytesSent, long totalBytesReceived, int pendingSendBytes, int pendingReceivedBytes) {
			SubscriptionId = subscriptionId;
			ConnectionId = connectionId;
			SubscriptionEndpoint = subscriptionEndpoint;
			SendQueueSize = sendQueueSize;
			TotalBytesSent = totalBytesSent;
			TotalBytesReceived = totalBytesReceived;
			PendingSendBytes = pendingSendBytes;
			PendingReceivedBytes = pendingReceivedBytes;
		}
	}
}
