// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane.StateMachine.LogEntries;

internal static class ReplicationHelpers {
	extension(IRaftCluster raft) {
		public ValueTask AddOrUpdateDatabaseAsync(string databaseId,
			string description,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<AddOrUpdateDatabase>(new() { DatabaseId = databaseId, Description = description })
					{ Term = raft.Term }, token);

		public async ValueTask<bool> RemoveDatabaseAsync(string databaseId,
			CancellationToken token) {
			var box = new StrongBox<bool>();
			await raft.ReplicateAsync(
				new ProtobufLogEntry<RemoveDatabase>(new() { DatabaseId = databaseId })
					{ Term = raft.Term, Context = box }, token);
			return box.Value;
		}

		public ValueTask AddOrUpdateDatabaseNodeAsync(string databaseId,
			EndPoint address,
			bool isReadOnlyReplica,
			CancellationToken token)
			=> raft.ReplicateAsync(
				new ProtobufLogEntry<AddOrUpdateDatabaseNode>(new()
						{ Address = address.ToByteString(), DatabaseId = databaseId, IsReadOnlyReplica = isReadOnlyReplica })
					{ Term = raft.Term }, token);

		public async ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId,
			EndPoint address,
			CancellationToken token) {
			var box = new StrongBox<bool>();
			await raft.ReplicateAsync(
				new ProtobufLogEntry<RemoveDatabaseNode>(new()
						{ Address = address.ToByteString(), DatabaseId = databaseId })
					{ Term = raft.Term, Context = box }, token);
			return box.Value;
		}

		public async ValueTask<bool> AppointLeaderAsync(string databaseId,
			ulong epoch,
			EndPoint address,
			CancellationToken token) {
			var box = new StrongBox<bool>();
			await raft.ReplicateAsync(
				new ProtobufLogEntry<AppointLeader>(new()
						{ Address = address.ToByteString(), DatabaseId = databaseId, Epoch = epoch })
					{ Term = raft.Term, Context = box }, token);
			return box.Value;
		}
	}
}
