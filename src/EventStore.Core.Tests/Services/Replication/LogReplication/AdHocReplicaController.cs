// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using EventStore.Core.Services.VNode;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

internal class AdHocReplicaController<TStreamId> : IAsyncHandle<Message> {
	private readonly IQueuedHandler _inputQueue;
	private readonly IPublisher _outputBus;
	private readonly LeaderInfo<TStreamId> _leaderInfo;
	private readonly VNodeFSM _fsm;

	private VNodeState _state;

	private Guid _subscriptionId;
	private readonly object _subscriptionIdLock = new();
	private Guid SubscriptionId {
		get {
			lock (_subscriptionIdLock) {
				return _subscriptionId;
			}
		}
		set {
			lock (_subscriptionIdLock) {
				_subscriptionId = value;
			}
		}
	}

	private int _numWriterFlushes;

	public readonly AutoResetEvent ConnectionToLeaderEstablished = new(false);
	public ReplicationAck LastAck { get; private set; } = new();
	public IPublisher Publisher => _inputQueue;
	public int NumWriterFlushes => Interlocked.CompareExchange(ref _numWriterFlushes, 0, 0);

	public AdHocReplicaController(IPublisher outputBus, LeaderInfo<TStreamId> leaderInfo) {
		_inputQueue = new QueuedHandlerThreadPool(
			consumer: this,
			name: "InputQueue",
			queueStatsManager: new QueueStatsManager(),
			trackers: new QueueTrackers());
		_outputBus = outputBus;
		_leaderInfo = leaderInfo;

		_state = VNodeState.Initializing;
		SubscriptionId = Guid.Empty;

		_fsm = new VNodeFSMBuilder(new(this, in _state))
			.InAnyState()
			.When<SystemMessage.VNodeConnectionEstablished>().Do(Handle)
			.When<SystemMessage.BecomePreReplica>().Do(Handle)
			.When<ReplicationMessage.SubscribeToLeader>().Do(Handle)
			.When<ReplicationMessage.ReplicaSubscribed>().Do(Handle)
			.When<ReplicationMessage.CloneAssignment>().Do(Handle)
			.When<ReplicationMessage.FollowerAssignment>().Do(Handle)
			.When<ReplicationMessage.CreateChunk>().Do(Handle)
			.When<ReplicationMessage.DataChunkBulk>().Do(Handle)
			.When<ReplicationMessage.RawChunkBulk>().Do(Handle)
			.When<ReplicationMessage.AckLogPosition>().Do(Handle)
			.When<ReplicationTrackingMessage.WriterCheckpointFlushed>().Do(Handle)
			.WhenOther().ForwardTo(_outputBus)
			.Build();

		_inputQueue.Start();
	}

	public void ResetSubscription() {
		SubscriptionId = Guid.Empty;
	}

	public ValueTask HandleAsync(Message message, CancellationToken token)
		=> _fsm.HandleAsync(message, token);

	private void Handle(SystemMessage.VNodeConnectionEstablished message) {
		ConnectionToLeaderEstablished.Set();
		_outputBus.Publish(message);
	}

	private void Handle(SystemMessage.BecomePreReplica message) {
		_state = VNodeState.PreReplica;
		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.SubscribeToLeader message) {
		SubscriptionId = message.SubscriptionId;
		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.ReplicaSubscribed message) {
		if (SubscriptionId != message.SubscriptionId)
			return;

		_state = VNodeState.CatchingUp;
		_outputBus.Publish(new SystemMessage.BecomeCatchingUp(
			correlationId: Guid.NewGuid(),
			leader: _leaderInfo.MemberInfo));
		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.CloneAssignment message) {
		if (SubscriptionId != message.SubscriptionId)
			return;

		_state = VNodeState.Clone;
		_outputBus.Publish(new SystemMessage.BecomeClone(
			correlationId: Guid.NewGuid(),
			leader: _leaderInfo.MemberInfo));
		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.FollowerAssignment message) {
		if (SubscriptionId != message.SubscriptionId)
			return;

		_state = VNodeState.Follower;
		_outputBus.Publish(new SystemMessage.BecomeFollower(
			correlationId: Guid.NewGuid(),
			leader: _leaderInfo.MemberInfo));
		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.CreateChunk message) {
		if (SubscriptionId != message.SubscriptionId || !_state.IsReplica())
			return;

		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.DataChunkBulk message) {
		if (SubscriptionId != message.SubscriptionId || !_state.IsReplica())
			return;

		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.RawChunkBulk message) {
		if (SubscriptionId != message.SubscriptionId || !_state.IsReplica())
			return;

		_outputBus.Publish(message);
	}

	private void Handle(ReplicationMessage.AckLogPosition message) {
		if (SubscriptionId != message.SubscriptionId || !_state.IsReplica())
			return;

		LastAck = new ReplicationAck {
			ReplicationPosition = message.ReplicationLogPosition,
			WriterPosition = message.WriterLogPosition
		};

		_outputBus.Publish(message);
	}

	private void Handle(ReplicationTrackingMessage.WriterCheckpointFlushed message) {
		Interlocked.Increment(ref _numWriterFlushes);
		_outputBus.Publish(message);
	}
}
