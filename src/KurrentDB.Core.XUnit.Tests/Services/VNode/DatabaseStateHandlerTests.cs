// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.EpochManager;
using KurrentDB.Core.Services.VNode;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.KontrolPlane;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.VNode;

public class DatabaseStateHandlerTests {
	private static readonly Guid ThisInstanceId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
	private static readonly Guid LeaderInstanceId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
	private const int NodePriority = 3;
	private const ulong Epoch = 5;

	private readonly FakePublisher _publisher = new();
	private readonly StubEpochManager _epochManager = new();
	private readonly InMemoryCheckpoint _writerCheckpoint = new();
	private readonly InMemoryCheckpoint _chaserCheckpoint = new();

	private DatabaseStateHandler CreateSut(bool readOnlyReplica = false) => new(
		mainQueue: _publisher,
		epochManager: _epochManager,
		writerCheckpoint: _writerCheckpoint,
		chaserCheckpoint: _chaserCheckpoint,
		currentNode: CurrentNode(readOnlyReplica),
		nodePriority: NodePriority);

	private static DatabaseNode CurrentNode(bool readOnlyReplica = false) => new() {
		DatabaseId = Database.MainDatabaseId,
		Address = new DnsEndPoint("this-node", 1113),
		ReplicationProtocolAddress = new DnsEndPoint("this-node", 1112),
		InstanceId = ThisInstanceId,
		Role = readOnlyReplica ? DatabaseNodeRole.ReadOnlyReplica : DatabaseNodeRole.Regular,
	};

	private static DatabaseNode LeaderNode() => new() {
		DatabaseId = Database.MainDatabaseId,
		Address = new DnsEndPoint("leader", 2113),
		ReplicationProtocolAddress = new DnsEndPoint("leader", 2112),
		InstanceId = LeaderInstanceId,
	};

	private static Database MainDatabase() => new() { Id = Database.MainDatabaseId, Epoch = Epoch };

	private static DatabaseCluster MainCluster() => new() { Id = Database.MainDatabaseId, Epoch = Epoch };

	// RunLeadershipAsync does not consume the change stream yet.
	private static async IAsyncEnumerable<DatabaseCluster> NoChanges() {
		await Task.CompletedTask;
		yield break;
	}

	// FakePublisher's list is written by the handler's task, so index rather than enumerate.
	private async Task<T> WaitForPublished<T>() where T : Message {
		var deadline = DateTime.UtcNow + Timeout;
		while (DateTime.UtcNow < deadline) {
			if (FindPublished<T>() is { } match)
				return match;

			await Task.Delay(10);
		}

		Assert.Fail($"{typeof(T).Name} was never published.");
		return null;
	}

	// The controller answers Freeze once it has taken the node out of service. Freeze now blocks on
	// that reply, so a test that lets a handler unwind has to play that part.
	private async Task AnswerFreeze() =>
		(await WaitForPublished<SystemMessage.Freeze>()).Envelope.ReplyWith(SystemMessage.Frozen.Instance);

	private T FindPublished<T>() where T : Message {
		for (var i = 0; i < _publisher.Messages.Count; i++) {
			if (_publisher.Messages[i] is T match)
				return match;
		}

		return null;
	}

	[Fact]
	public async Task reports_the_unflushed_checkpoints() {
		// Written but not flushed: reading the flushed value would report 0 for both.
		_writerCheckpoint.Write(500);
		_chaserCheckpoint.Write(400);

		var state = await CreateSut().GetReplicaStateAsync(CancellationToken.None);

		Assert.Equal(500, state.WriterCheckpoint);
		Assert.Equal(400, state.ChaserCheckpoint);
	}

	[Fact]
	public async Task reports_epoch_zero_when_no_epoch_has_been_written() {
		// The epoch manager reports -1, which the Kontrol Plane's unsigned epoch cannot express.
		var state = await CreateSut().GetReplicaStateAsync(CancellationToken.None);

		Assert.Equal(0UL, state.Epoch);
	}

	[Fact]
	public async Task reports_the_last_written_epoch() {
		_epochManager.LastEpochNumber = 7;

		var state = await CreateSut().GetReplicaStateAsync(CancellationToken.None);

		Assert.Equal(7UL, state.Epoch);
	}

	[Fact]
	public async Task reports_this_nodes_ephemeral_instance_id() {
		// The Kontrol Plane matches this against the appointment on renewal, so that a node which has
		// restarted cannot inherit the appointment made for its pre-restart self.
		var state = await CreateSut().GetReplicaStateAsync(CancellationToken.None);

		Assert.Equal(ThisInstanceId, state.InstanceId);
		Assert.NotEqual(LeaderInstanceId, state.InstanceId);
	}

	[Fact]
	public async Task reports_the_node_priority() {
		var sut = CreateSut();

		var initial = await sut.GetReplicaStateAsync(CancellationToken.None);
		Assert.Equal(NodePriority, initial.Priority);

		sut.Handle(new GossipMessage.UpdateNodePriority(9));

		var updated = await sut.GetReplicaStateAsync(CancellationToken.None);
		Assert.Equal(9, updated.Priority);
	}

	[Fact]
	public async Task announces_the_appointed_leader_before_replicating() {
		var sut = CreateSut();
		using var cts = new CancellationTokenSource();

		var replicating = sut.RunReplicationAsync(MainDatabase(), LeaderNode(), cts.Token);

		var appointed = await WaitForPublished<ElectionMessage.LeaderAppointed>();
		Assert.Equal((int)Epoch, appointed.EpochNumber);
		Assert.Equal(LeaderInstanceId, appointed.Leader.InstanceId);
		Assert.Equal(LeaderNode().ReplicationProtocolAddress, appointed.Leader.ReplicationEndPoint);

		await cts.CancelAsync();
		await AnswerFreeze();
		await replicating.WaitAsync(Timeout);
	}

	[Fact]
	public async Task freezes_when_replication_ends() {
		var sut = CreateSut();
		using var cts = new CancellationTokenSource();

		var replicating = sut.RunReplicationAsync(MainDatabase(), LeaderNode(), cts.Token);
		await WaitForPublished<ElectionMessage.LeaderAppointed>();
		await cts.CancelAsync();

		// It asks to be taken out of service, and does not return until that has been answered - so
		// the caller knows the node has stopped, not merely that it was asked to.
		var freeze = await WaitForPublished<SystemMessage.Freeze>();
		await Task.Delay(50);
		Assert.False(replicating.IsCompleted);

		freeze.Envelope.ReplyWith(SystemMessage.Frozen.Instance);
		await replicating.WaitAsync(Timeout);
	}

	[Fact]
	public async Task read_only_replica_does_not_freeze_when_replication_ends() {
		// A read-only replica is never fenced, so it has nothing to prove by stopping - and staying
		// connected lets the next appointment swap the leader over directly.
		var sut = CreateSut(readOnlyReplica: true);
		using var cts = new CancellationTokenSource();

		var replicating = sut.RunReplicationAsync(MainDatabase(), LeaderNode(), cts.Token);
		await WaitForPublished<ElectionMessage.LeaderAppointed>();
		await cts.CancelAsync();

		await replicating.WaitAsync(Timeout);
		Assert.Null(FindPublished<SystemMessage.Freeze>());
	}

	[Fact]
	public async Task announces_itself_as_leader() {
		var sut = CreateSut();
		using var cts = new CancellationTokenSource();

		var leading = sut.RunLeadershipAsync(MainCluster(), NoChanges(), cts.Token);

		var appointed = await WaitForPublished<ElectionMessage.LeaderAppointed>();
		Assert.Equal((int)Epoch, appointed.EpochNumber);
		Assert.Equal(ThisInstanceId, appointed.Leader.InstanceId);

		await cts.CancelAsync();
		await AnswerFreeze();
		await AssertCompletes(leading);
	}

	[Fact]
	public async Task leadership_continues_until_the_appointment_is_answered() {
		var sut = CreateSut();
		using var cts = new CancellationTokenSource();

		var leading = sut.RunLeadershipAsync(MainCluster(), NoChanges(), cts.Token);
		var appointed = await WaitForPublished<ElectionMessage.LeaderAppointed>();

		await Task.Delay(50);
		Assert.False(leading.IsCompleted);

		// Answering is how the controller reports that the node has left the leader states.
		appointed.Envelope.ReplyWith(ElectionMessage.LeadershipEnded.Instance);

		// Completes on its own, without the token being cancelled.
		await AnswerFreeze();
		await leading.WaitAsync(Timeout);
		Assert.False(cts.IsCancellationRequested);
	}

	[Fact]
	public async Task leadership_ends_when_cancelled() {
		var sut = CreateSut();
		using var cts = new CancellationTokenSource();

		var leading = sut.RunLeadershipAsync(MainCluster(), NoChanges(), cts.Token);
		await WaitForPublished<ElectionMessage.LeaderAppointed>();

		await cts.CancelAsync();
		await AnswerFreeze();
		await AssertCompletes(leading);
	}

	private static async Task AssertCompletes(Task task) {
		try {
			await task.WaitAsync(Timeout);
		} catch (OperationCanceledException) {
			// the handler may surface the cancellation or swallow it; both end the appointment
		}

		Assert.True(task.IsCompleted);
	}

	// The handler only ever reads LastEpochNumber.
	private sealed class StubEpochManager : IEpochManager {
		public int LastEpochNumber { get; set; } = -1;

		public ValueTask Init(CancellationToken token) => throw new NotSupportedException();
		public EpochRecord GetLastEpoch() => throw new NotSupportedException();
		public ValueTask<IReadOnlyList<EpochRecord>> GetLastEpochs(int maxCount, CancellationToken token) =>
			throw new NotSupportedException();
		public ValueTask<EpochRecord> GetEpochAfter(int epochNumber, bool throwIfNotFound, CancellationToken token) =>
			throw new NotSupportedException();
		public ValueTask<bool> IsCorrectEpochAt(long epochPosition, int epochNumber, Guid epochId, CancellationToken token) =>
			throw new NotSupportedException();
		public ValueTask WriteNewEpoch(int epochNumber, CancellationToken token) => throw new NotSupportedException();
		public ValueTask CacheEpoch(EpochRecord epoch, CancellationToken token) => throw new NotSupportedException();
		public ValueTask<EpochRecord> TryTruncateBefore(long position, CancellationToken token) =>
			throw new NotSupportedException();
	}
}
