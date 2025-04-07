// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.LeaderReplication;


public class when_replica_subscribes_with_no_common_epochs<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out _);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(1);

		var epochs = new[] {
			new Epoch(1010, 1, Guid.NewGuid()),
			new Epoch(999, 2, Guid.NewGuid())
		};

		AddSubscription(_replicaId, true, epochs, 1010, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_a_replica_subscribed_message_from_start() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.Zero(subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_with_same_epochs_subscribes_from_last_epoch_position<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private EpochRecord _lastEpoch;

	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out _);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(1);

		_lastEpoch = EpochManager.GetLastEpoch();
		var epochs = EpochManager.GetLastEpochs(10)
			.Select(e => new Epoch(e.EpochPosition, e.EpochNumber, e.EpochId)).ToArray();

		AddSubscription(_replicaId, true, epochs, _lastEpoch.EpochPosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_a_replica_subscribed_message_from_last_epoch_position() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(_lastEpoch.EpochPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_with_same_epochs_subscribes_from_position_less_than_last_epoch_position<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private EpochRecord _lastEpoch;
	private long _subscribedPosition;

	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out _);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _subscribedPosition);
		EpochManager.WriteNewEpoch(1);

		_lastEpoch = EpochManager.GetLastEpoch();
		var epochs = EpochManager.GetLastEpochs(10)
			.Select(e => new Epoch(e.EpochPosition, e.EpochNumber, e.EpochId)).ToArray();

		AddSubscription(_replicaId, true, epochs, _subscribedPosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_a_replica_subscribed_message_from_requested_position() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(_subscribedPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_with_additional_epochs_subscribes_to_position_past_leaders_last_epoch<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private List<Epoch> _replicaEpochs;

	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out _);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(1);
		Writer.Write(CreateLogRecord(5), out _);
		Writer.Write(CreateLogRecord(6), out _);
		Writer.Write(CreateLogRecord(7), out var lastWritePosition);
		Writer.Flush();

		_replicaEpochs = new List<Epoch> {
			new Epoch(lastWritePosition + 2000, 4, Guid.NewGuid()),
			new Epoch(lastWritePosition + 1000, 3, Guid.NewGuid()),
			new Epoch(lastWritePosition, 2, Guid.NewGuid()),
		};
		_replicaEpochs.AddRange(EpochManager.GetLastEpochs(10)
			.Select(e => new Epoch(e.EpochPosition, e.EpochNumber, e.EpochId)).ToList());

		AddSubscription(_replicaId, true, _replicaEpochs.ToArray(), lastWritePosition + 2000, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_replica_subscribed_message_for_epoch_after_common_epoch() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(_replicaEpochs[2].EpochPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_subscribes_with_epoch_that_doesnt_exist_on_leader_but_is_before_leaders_last_epoch<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private List<Epoch> _replicaEpochs;

	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out var otherEpochLogPosition);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(2);

		var firstEpoch = EpochManager.GetLastEpochs(10).First(e => e.EpochNumber == 0);
		_replicaEpochs = new List<Epoch> {
			new Epoch(otherEpochLogPosition, 1, Guid.NewGuid()),
			new Epoch(firstEpoch.EpochPosition, firstEpoch.EpochNumber, firstEpoch.EpochId)
		};

		AddSubscription(_replicaId, true, _replicaEpochs.ToArray(), _replicaEpochs[1].EpochPosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_replica_subscribed_message_for_epoch_after_common_epoch() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(_replicaEpochs[0].EpochPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_subscribes_with_additional_epoch_past_leaders_writer_checkpoint<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private List<Epoch> _replicaEpochs;

	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out _);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(1);

		var subscribePosition = Writer.Position + 1000;
		_replicaEpochs = new List<Epoch> {
			new Epoch(subscribePosition, 2, Guid.NewGuid()),
		};
		_replicaEpochs.AddRange(EpochManager.GetLastEpochs(10)
			.Select(e => new Epoch(e.EpochPosition, e.EpochNumber, e.EpochId)).ToList());

		AddSubscription(_replicaId, true, _replicaEpochs.ToArray(), subscribePosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_replica_subscribed_message_for_leaders_writer_checkpoint() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(Writer.Position, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_subscribes_with_additional_epoch_and_leader_has_epoch_after_common_epoch<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private List<Epoch> _replicaEpochs;

	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		Writer.Write(CreateLogRecord(1), out _);
		Writer.Write(CreateLogRecord(2), out _);
		EpochManager.WriteNewEpoch(1);
		Writer.Write(CreateLogRecord(3), out _);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(4);

		var subscribePosition = Writer.Position + 1000;
		_replicaEpochs = new List<Epoch> {
			new Epoch(subscribePosition, 2, Guid.NewGuid()),
		};
		_replicaEpochs.AddRange(EpochManager.GetLastEpochs(10)
			.Where(e => e.EpochNumber < 4)
			.Select(e => new Epoch(e.EpochPosition, e.EpochNumber, e.EpochId)).ToList());

		AddSubscription(_replicaId, true, _replicaEpochs.ToArray(), subscribePosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_replica_subscribed_message_for_leaders_epoch_after_common_epoch() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(EpochManager.GetLastEpoch().EpochPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_subscribes_with_uncached_epoch<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private List<Epoch> _replicaEpochs;
	public override void When() {
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		EpochManager.WriteNewEpoch(1);

		// The EpochManager for these tests only caches 5 epochs
		_replicaEpochs = EpochManager.GetLastEpochs(2)
			.Select(e => new Epoch(e.EpochPosition, e.EpochNumber, e.EpochId)).ToList();

		Writer.Write(CreateLogRecord(1), out _);
		EpochManager.WriteNewEpoch(2);
		Writer.Write(CreateLogRecord(2), out _);
		EpochManager.WriteNewEpoch(3);
		Writer.Write(CreateLogRecord(3), out _);
		EpochManager.WriteNewEpoch(4);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(5);
		Writer.Write(CreateLogRecord(5), out _);
		EpochManager.WriteNewEpoch(6);

		AddSubscription(_replicaId, true, _replicaEpochs.ToArray(), _replicaEpochs[0].EpochPosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_a_replica_subscribed_message_common_epoch() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(_replicaEpochs[0].EpochPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}

public class when_replica_subscribes_with_uncached_epoch_that_does_not_exist_on_leader<TLogFormat, TStreamId>
	: with_replication_service_and_epoch_manager<TLogFormat, TStreamId> {
	private readonly Guid _replicaId = Guid.NewGuid();
	private TcpConnectionManager _replicaManager;
	private List<Epoch> _replicaEpochs;
	private EpochRecord[] _uncachedLeaderEpochs;
	public override void When() {
		// The EpochManager for these tests only caches 5 epochs
		// Epochs 2 and 3 don't exist
		EpochManager.WriteNewEpoch(0);
		Writer.Write(CreateLogRecord(0), out _);
		EpochManager.WriteNewEpoch(1);
		Writer.Write(CreateLogRecord(1), out _);

		_uncachedLeaderEpochs = EpochManager.GetLastEpochs(2);

		EpochManager.WriteNewEpoch(4);
		Writer.Write(CreateLogRecord(2), out _);
		EpochManager.WriteNewEpoch(5);
		Writer.Write(CreateLogRecord(3), out _);
		EpochManager.WriteNewEpoch(6);
		Writer.Write(CreateLogRecord(4), out _);
		EpochManager.WriteNewEpoch(7);
		Writer.Write(CreateLogRecord(5), out _);
		EpochManager.WriteNewEpoch(8);

		_replicaEpochs = new List<Epoch> {
			new Epoch(_uncachedLeaderEpochs[0].EpochPosition + 8000, 3, Guid.NewGuid()),
			new Epoch(_uncachedLeaderEpochs[0].EpochPosition + 4000, 2, Guid.NewGuid()),
			new Epoch(_uncachedLeaderEpochs[0].EpochPosition, _uncachedLeaderEpochs[0].EpochNumber, _uncachedLeaderEpochs[0].EpochId),
			new Epoch(_uncachedLeaderEpochs[1].EpochPosition, _uncachedLeaderEpochs[1].EpochNumber, _uncachedLeaderEpochs[1].EpochId)
		};

		AddSubscription(_replicaId, true, _replicaEpochs.ToArray(), _replicaEpochs[0].EpochPosition, out _replicaManager);
	}

	[Test]
	public void subscription_is_sent_a_replica_subscribed_message_to_epoch_position_after_common_epoch() {
		var message = GetTcpSendsFor(_replicaManager).Select(x => x.Message).First();

		Assert.IsInstanceOf<ReplicationMessage.ReplicaSubscribed>(message);
		var subscribed = (ReplicationMessage.ReplicaSubscribed)message;
		Assert.AreEqual(EpochManager.GetLastEpochs(5).First(x => x.EpochNumber == 4).EpochPosition, subscribed.SubscriptionPosition);
		Assert.AreEqual(_replicaId, subscribed.SubscriptionId);
		Assert.AreEqual(LeaderId, subscribed.LeaderId);
	}
}
