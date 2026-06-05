// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane.StateMachine.LogEntries;

internal static class ReplicationHelpers {
	extension(IRaftCluster raft)
	{
		public ValueTask AddOrUpdateDatabaseAsync(string databaseId,
			string description,
			TaskCompletionSource<bool> completion,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<AddOrUpdateDatabase>(new() { DatabaseId = databaseId, Description = description })
					{ Term = raft.Term, Context = completion }, token);

		public ValueTask RemoveDatabaseAsync(string databaseId,
			TaskCompletionSource<bool> completion,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<RemoveDatabase>(new() { DatabaseId = databaseId })
					{ Term = raft.Term, Context = completion }, token);

		public ValueTask AddOrUpdateDatabaseNodeAsync(string databaseId,
			IDatabaseNode node,
			TaskCompletionSource<bool> completion,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<AddOrUpdateDatabaseNode>(new()
						{ Address = node.Address.ToByteString(), DatabaseId = databaseId, IsReadOnlyReplica = node.IsReadOnlyReplica })
					{ Term = raft.Term, Context = completion }, token);

		public ValueTask RemoveDatabaseNodeAsync(string databaseId,
			EndPoint address,
			TaskCompletionSource<bool> completion,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<RemoveDatabaseNode>(new()
						{ Address = address.ToByteString(), DatabaseId = databaseId })
					{ Term = raft.Term, Context = completion }, token);

		public ValueTask AppointLeaderAsync(string databaseId,
			EndPoint address,
			ulong expectedVersion,
			TaskCompletionSource<bool> completion,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<AppointLeader>(new()
						{ Address = address.ToByteString(), DatabaseId = databaseId, ExpectedVersion = expectedVersion })
					{ Term = raft.Term, Context = completion }, token);
	}
}
