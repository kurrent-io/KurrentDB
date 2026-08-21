// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.EpochManager;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.DataPlane;
using KurrentDB.KontrolPlane;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.VNode;

/// <summary>
/// Drives this node's <see cref="VNodeFSM"/> from the leader appointments made by the Kontrol Plane.
/// </summary>
/// <remarks>
/// <para>
/// Threading:
///   - CurrentNode and _nodePriority are written serially but can be read concurrently.
///   - The <see cref="IDatabaseStateHandler"/> methods are called serially.
/// </para>
/// </remarks>
public sealed class DatabaseStateHandler :
	IDatabaseStateHandler,
	IHandle<GossipMessage.UpdateNodePriority> {

	private static readonly ILogger Log = Serilog.Log.ForContext<DatabaseStateHandler>();

	private readonly IPublisher _mainQueue;
	private readonly IEpochManager _epochManager;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly IReadOnlyCheckpoint _chaserCheckpoint;
	private int _nodePriority;

	public DatabaseStateHandler(
		IPublisher mainQueue,
		IEpochManager epochManager,
		IReadOnlyCheckpoint writerCheckpoint,
		IReadOnlyCheckpoint chaserCheckpoint,
		DatabaseNode currentNode,
		int nodePriority) {

		_mainQueue = Ensure.NotNull(mainQueue);
		_epochManager = Ensure.NotNull(epochManager);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_chaserCheckpoint = Ensure.NotNull(chaserCheckpoint);
		CurrentNode = Ensure.NotNull(currentNode);
		_nodePriority = nodePriority;
	}

	/// <inheritdoc/>
	public DatabaseNode CurrentNode { get; set; }

	private bool IsReadOnlyReplica =>
		CurrentNode.Role is DatabaseNodeRole.ReadOnlyReplica;

	public void Handle(GossipMessage.UpdateNodePriority message) =>
		_nodePriority = message.NodePriority;

	/// <inheritdoc/>
	public ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token) {
		// when no epochs have been written LastEpochNumber is -1.
		// bumping it to 0 is safe because writer checkpoint will also reflect that no epoch is written.
		var epoch = (ulong)Math.Max(0, _epochManager.LastEpochNumber); 
		return ValueTask.FromResult(new ReplicaState(
			Epoch: epoch,
			WriterCheckpoint: _writerCheckpoint.ReadNonFlushed(), // followers ack unflushed
			ChaserCheckpoint: _chaserCheckpoint.ReadNonFlushed(), // chaser not important for correctness just speed of transition
			Priority: _nodePriority,
			InstanceId: CurrentNode.InstanceId));
	}

	/// <inheritdoc/>
	public async Task RunReplicationAsync(Database database, DatabaseNode leaderNode, CancellationToken token) {
		try {
			var epochNumber = ToEpochNumber(database.Epoch);
			Log.Information(
				"========== [{httpEndPoint}] KONTROL PLANE APPOINTED [{leaderAddress}] AS LEADER of database {databaseId} at epoch {epoch}.",
				CurrentNode.Address, leaderNode.Address, database.Id, epochNumber);

			// Notify the node of who was appointed leader. Replication begins.
			_mainQueue.Publish(new ElectionMessage.LeaderAppointed(
					epochNumber: epochNumber,
					leader: ToLeaderMemberInfo(leaderNode, epochNumber)));

			await token.WaitAsync();
		} finally {
			if (!IsReadOnlyReplica) {
				await Freeze();
			} else {
				// Don't freeze RoR. No need because they don't participate in committing new data
				// and there isn't currently  a state we can put them in where they will stay frozen.
			}
		}
	}

	/// <inheritdoc/>
	public async Task RunLeadershipAsync(
		DatabaseCluster initial,
		IAsyncEnumerable<DatabaseCluster> changes,
		CancellationToken token) {

		try {
			var epochNumber = ToEpochNumber(initial.Epoch);
			var thisNode = CurrentNode;
			Log.Information(
				"========== [{httpEndPoint}] KONTROL PLANE APPOINTED THIS NODE AS LEADER of database {databaseId} at epoch {epoch}.",
				thisNode.Address, initial.Id, epochNumber);

			// This transitions the node into PreLeader. The inauguration manager takes it from there
			// and moves us to Leader once the epoch record for this appointment has been replicated.
			// Alternatively inauguration may fail, moving us out of a leadership related state.
			var leadershipEnded = new TcsEnvelope<ElectionMessage.LeadershipEnded>();
			_mainQueue.Publish(new ElectionMessage.LeaderAppointed(
				epochNumber: epochNumber,
				leader: ToLeaderMemberInfo(thisNode, epochNumber),
				envelope: leadershipEnded));

			await leadershipEnded.Task.WaitAsync(token);
		} finally {
			await Freeze();
		}
	}

	// Stops replication in the sense of stopping this node from participating in the commit process.
	//
	// With a naive selection of a leader by highest (epoch, writer checkpoint) pair, the problem is that a
	// node might be appointed leader, then, before it actually becomes the leader, some additional data gets
	// committed on the previous epoch which then gets truncated on this epoch.
	//
	// We therefore stop the leader of the previous epoch from committing any extra data before we appoint a
	// new leader. This is called fence, which we require to be acked by the majority of the regular nodes.
	// We don't try to fence only the leader because it might not be reachable. The fence is an instruction
	// to the nodes to stop trying to get data committed for earlier epochs. When a node has done this it
	// returns its epoch and writer checkpoint which are an upper bound of what that node has acked
	// (i.e. we might have acked less, but we have not acked more).
	//
	// It is ok if the reported writer checkpoint is higher than what we acked, as long as we still have data
	// up to that point if we get appointed.
	//
	// We use the unflushed writer checkpoint in particular, because followers ack (allowing commit) before flushing.
	//
	// We just need to make sure that whatever we ack doesn't exceed the reported writer checkpoint.
	// - Previous ACKS may already be in the send queues or on the wire, but these are necessarily less than
	//   or equal to the writer checkpoint.
	// - At the time we read the writer checkpoint, further writes may be in the writer queue so the writer
	//   checkpoint may continue to move and even be flushed.
	//     - That's ok on followers because the subsequent ReplicationMessage.AckLogPosition messages are
	//       blocked by ClusterVNodeController
	//     - That's ok on the leader because the replication checkpoint is only advanced in Leader & Preleader
	//       (see ReplicationTrackingService)
	//     - ReadOnlyReplicas don't need to freeze because they never contribute to data becoming committed.
	private async ValueTask Freeze() {
		Debug.Assert(!IsReadOnlyReplica);

		var frozen = new TcsEnvelope<SystemMessage.Frozen>();
		_mainQueue.Publish(new SystemMessage.Freeze(frozen));
		await frozen.Task;
	}

	// The Kontrol Plane epoch is what the appointed leader writes as the epoch number of its epoch
	// record, so the two numbering schemes are the same one - only the widths differ.
	private static int ToEpochNumber(ulong epoch) => epoch <= int.MaxValue
		? (int)epoch
		: throw new InvalidOperationException(
			$"Kontrol Plane epoch {epoch} is larger than the largest epoch number this node can write ({int.MaxValue}).");

	private static MemberInfoLite ToLeaderMemberInfo(DatabaseNode node, int epochNumber) => new() {
		InstanceId = node.InstanceId,
		HttpEndPoint = node.Address,
		ReplicationEndPoint = node.ReplicationProtocolAddress,
		ClientHttpEndPoint = node.ClientApiAddress,
		ClientTcpPort = node.ClientTcpApiPort,
		ClientTcpApiIsSecure = node.ClientTcpApiIsSecure,
		EpochNumber = epochNumber,
	};
}
