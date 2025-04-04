// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Index;
using EventStore.Core.Services.Storage;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.TransactionLog.Checkpoint;
using NUnit.Framework;
// ReSharper disable ObjectCreationAsStatement

namespace EventStore.Core.Tests.Services.IndexCommitter;

[TestFixture]
public class when_creating_index_committer_service {
	protected ICheckpoint ReplicationCheckpoint = new InMemoryCheckpoint(0);
	protected ICheckpoint WriterCheckpoint = new InMemoryCheckpoint(0);
	protected IPublisher Publisher = new FakePublisher();
	protected FakeIndexCommitter<string> IndexCommitter = new();
	protected ITableIndex TableIndex = new FakeTableIndex<string>();
	private readonly QueueStatsManager _queueStatsManager = new();

	[Test]
	public void null_index_committer_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => new IndexCommitterService<string>(null, Publisher,
			 WriterCheckpoint, ReplicationCheckpoint, TableIndex, _queueStatsManager));
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => new IndexCommitterService<string>(IndexCommitter, null,
			 WriterCheckpoint, ReplicationCheckpoint, TableIndex, _queueStatsManager));
	}

	[Test]
	public void null_writer_checkpoint_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => new IndexCommitterService<string>(IndexCommitter, Publisher,
			 null, ReplicationCheckpoint, TableIndex, _queueStatsManager));
	}
	[Test]
	public void null_replication_checkpoint_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => new IndexCommitterService<string>(IndexCommitter, Publisher,
			 WriterCheckpoint, null, TableIndex, _queueStatsManager));
	}
}
