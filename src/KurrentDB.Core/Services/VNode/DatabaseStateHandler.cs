// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

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
	IHandle<ClientMessage.SetNodePriority>,
	IHandle<SystemMessage.SystemStart>,
	IHandle<SystemMessage.BecomeShuttingDown> {

	private static readonly ILogger Log = Serilog.Log.ForContext<DatabaseStateHandler>();

	private readonly IPublisher _mainQueue;
	private readonly IEpochManager _epochManager;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly IReadOnlyCheckpoint _chaserCheckpoint;
	private readonly CancellationTokenSource _cts = new();
	private readonly TaskCompletionSource _systemInitialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

	public void Handle(ClientMessage.SetNodePriority message) {
		Log.Information("Setting Node Priority to {nodePriority}.", message.NodePriority);
		_nodePriority = message.NodePriority;
		_mainQueue.Publish(new GossipMessage.UpdateNodePriority(message.NodePriority));
	}

	// System has initialized, is now being told to start.
	public void Handle(SystemMessage.SystemStart message) =>
		_systemInitialized.TrySetResult();

	public void Handle(SystemMessage.BecomeShuttingDown message) =>
		_cts.Cancel();

	/// <inheritdoc/>
	public ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token) {
		if (!_systemInitialized.Task.IsCompleted) {
			// we need epochManager to be initialized with the last epoch
			// this should never happen because the relevant gRPC service goes live after initialization
			throw new InvalidOperationException("System not yet initialized");
		}

		// when no epochs have been written LastEpochNumber is -1 but we need a ulong.
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
				"Kontrol plane appointed [{leaderAddress}] as leader of database '{databaseId}' at epoch {epoch}.",
				leaderNode.Address, database.Id, epochNumber);

			// Notify the node of who was appointed leader. Replication begins.
			await AnnounceLeaderAsync(leaderNode, epochNumber, envelope: null, token);

			await token.WaitAsync();
		} finally {
			if (!IsReadOnlyReplica) {
				await Freeze(token.IsCancellationRequested
					? FreezeTrigger.KPlane
					: FreezeTrigger.Unknown);
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

		var endedDueToVNodeStateTransition = false;
		try {
			var epochNumber = ToEpochNumber(initial.Epoch);
			var thisNode = CurrentNode;
			Log.Information(
				"Kontrol plane appointed this node as leader of database '{databaseId}' at epoch {epoch}.",
				initial.Id, epochNumber);

			// This transitions the node into PreLeader. The inauguration manager takes it from there
			// and moves us to Leader once the epoch record for this appointment has been replicated.
			// Alternatively inauguration may fail, moving us out of a leadership related state.
			var leadershipEnded = new TcsEnvelope<ElectionMessage.LeadershipEnded>();
			await AnnounceLeaderAsync(thisNode, epochNumber, leadershipEnded, token);

			await leadershipEnded.Task.WaitAsync(token);
			endedDueToVNodeStateTransition = true;
		} finally {
			var trigger = (endedDueToVNodeStateTransition, token.IsCancellationRequested) switch {
				(true, _) => FreezeTrigger.ClusterVNode,
				(_, true) => FreezeTrigger.KPlane,
				_ => FreezeTrigger.Unknown,
			};
			await Freeze(trigger);
		}
	}

	enum FreezeTrigger {
		Unknown,
		ClusterVNode,
		KPlane,
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
	private async ValueTask Freeze(FreezeTrigger trigger) {
		Debug.Assert(!IsReadOnlyReplica);

		Log.Debug("Freezing. Trigger: {trigger}...", trigger);
		var frozen = new TcsEnvelope<SystemMessage.Frozen>();
		_mainQueue.Publish(new SystemMessage.Freeze(frozen));

		try {
			// if we are ShuttingDown then we may not get a reply to the freeze
			// but we are already frozen (the ClusterVNodeController has already transitioned)
			await frozen.Task.WaitAsync(_cts.Token);
			Log.Debug("Frozen");
		} catch (OperationCanceledException oce) when (oce.CancellationToken == _cts.Token) {
			Log.Debug("Freeze cancelled for shutdown");
		}
	}

	// Tells the node who the Kontrol Plane has appointed, once it is in a state to hear it.
	private async Task AnnounceLeaderAsync(
		DatabaseNode leaderNode,
		int epochNumber,
		IEnvelope<ElectionMessage.LeadershipEnded>? envelope,
		CancellationToken token) {

		if (!_systemInitialized.Task.IsCompleted) {
			// be ready for LeaderAppointed message
			Log.Information("Waiting for system to be initialized before announcing the appointed leader...");
			await _systemInitialized.Task.WaitAsync(token);
			Log.Information("System initialized, announcing the appointed leader.");
		}

		_mainQueue.Publish(new ElectionMessage.LeaderAppointed(
			epochNumber: epochNumber,
			leader: ToLeaderMemberInfo(leaderNode, epochNumber),
			envelope: envelope));
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
