// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Tests.Services.ElectionsService;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Chaser;

public abstract class with_storage_chaser_service<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	readonly ICheckpoint _writerChk = new InMemoryCheckpoint(Checkpoint.Writer);
	readonly ICheckpoint _chaserChk = new InMemoryCheckpoint(Checkpoint.Chaser);
	readonly ICheckpoint _epochChk = new InMemoryCheckpoint(Checkpoint.Epoch, initValue: -1);
	readonly ICheckpoint _proposalChk = new InMemoryCheckpoint(Checkpoint.Proposal, initValue: -1);
	readonly ICheckpoint _truncateChk = new InMemoryCheckpoint(Checkpoint.Truncate, initValue: -1);
	readonly ICheckpoint _replicationCheckpoint = new InMemoryCheckpoint(-1);
	readonly ICheckpoint _indexCheckpoint = new InMemoryCheckpoint(-1);
	readonly ICheckpoint _streamExistenceFilterCheckpoint = new InMemoryCheckpoint(-1);

	protected SynchronousScheduler Publisher = new("publisher");
	protected StorageChaser<TStreamId> Service;
	protected FakeIndexCommitterService<TStreamId> IndexCommitter;
	protected IEpochManager EpochManager;
	protected TFChunkDb Db;
	protected TFChunkChaser Chaser;
	protected TFChunkWriter Writer;

	protected ConcurrentQueue<StorageMessage.PrepareAck> PrepareAcks = new();
	protected ConcurrentQueue<StorageMessage.CommitAck> CommitAcks = new();

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		Db = new TFChunkDb(CreateDbConfig());
		await Db.Open();
		Chaser = new TFChunkChaser(Db, _writerChk, _chaserChk);
		Chaser.Open();
		Writer = new TFChunkWriter(Db);
		await Writer.Open(CancellationToken.None);

		IndexCommitter = new FakeIndexCommitterService<TStreamId>();
		EpochManager = new FakeEpochManager();

		Service = new StorageChaser<TStreamId>(
			Publisher,
			_writerChk,
			Chaser,
			IndexCommitter,
			EpochManager,
			new QueueStatsManager());

		Service.Handle(new SystemMessage.SystemStart());
		Service.Handle(new SystemMessage.SystemInit());

		Publisher.Subscribe(new AdHocHandler<StorageMessage.CommitAck>(CommitAcks.Enqueue));
		Publisher.Subscribe(new AdHocHandler<StorageMessage.PrepareAck>(PrepareAcks.Enqueue));

		await When(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await base.TestFixtureTearDown();
		Service.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), true, true));
	}


	public abstract ValueTask When(CancellationToken token);

	private TFChunkDbConfig CreateDbConfig() {

		var nodeConfig = new TFChunkDbConfig(
			PathName,
			1000,
			10000,
			_writerChk,
			_chaserChk,
			_epochChk,
			_proposalChk,
			_truncateChk,
			_replicationCheckpoint,
			_indexCheckpoint,
			_streamExistenceFilterCheckpoint,
			true);
		return nodeConfig;
	}
}
