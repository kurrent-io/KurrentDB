// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Net;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Replication;
using EventStore.Core.TransactionLog.Checkpoint;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.ReplicationTracking;

public abstract class with_clustered_replication_tracking_service:
	IHandle<ReplicationTrackingMessage.ReplicatedTo> {
	protected string EventStreamId = "test_stream";
	protected SynchronousScheduler Publisher = new("publisher");
	protected ReplicationTrackingService Service;
	protected ConcurrentQueue<ReplicationTrackingMessage.ReplicatedTo> ReplicatedTos = new ConcurrentQueue<ReplicationTrackingMessage.ReplicatedTo>();
	protected ICheckpoint ReplicationCheckpoint = new InMemoryCheckpoint();
	protected ICheckpoint WriterCheckpoint = new InMemoryCheckpoint();

	protected abstract int ClusterSize { get; }

	[OneTimeSetUp]
	public virtual void TestFixtureSetUp() {
		Publisher.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(this);

		Service = new ReplicationTrackingService(Publisher, ClusterSize,ReplicationCheckpoint, WriterCheckpoint);
		Service.Start();
		When();
	}

	[OneTimeTearDown]
	public virtual void TestFixtureTearDown() {
		Service.Stop();
	}

	public abstract void When();

	protected void BecomeLeader() {
		Service.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
	}

	protected void BecomeUnknown() {
		Service.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
	}

	public void Handle(ReplicationTrackingMessage.ReplicatedTo message) {
		ReplicatedTos.Enqueue(message);
	}
}
