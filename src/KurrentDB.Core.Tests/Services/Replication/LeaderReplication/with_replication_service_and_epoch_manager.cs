// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.Authentication.InternalAuthentication;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Replication;
using KurrentDB.Core.Services.Storage.EpochManager;
using KurrentDB.Core.Services.Transport.Tcp;
using KurrentDB.Core.Settings;
using KurrentDB.Core.Tests.Authentication;
using KurrentDB.Core.Tests.Authorization;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.Tests.Services.Transport.Tcp;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Replication.LeaderReplication;


[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public abstract class with_replication_service_and_epoch_manager<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private const int _connectionPendingSendBytesThreshold = 10 * 1024;
	private const int _connectionQueueSizeThreshold = 50000;

	protected int ClusterSize = 3;
	protected SynchronousScheduler Publisher = new("publisher");
	protected SynchronousScheduler TcpSendPublisher = new("tcpSend");
	protected LeaderReplicationService Service;
	protected ConcurrentQueue<TcpMessage.TcpSend> TcpSends = new ConcurrentQueue<TcpMessage.TcpSend>();
	protected LogFormatAbstractor<TStreamId> _logFormat;
	protected Guid LeaderId = Guid.NewGuid();

	protected TFChunkDbConfig DbConfig;
	protected EpochManager<TStreamId> EpochManager;
	protected TFChunkDb Db;
	protected TFChunkWriter Writer;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		TcpSendPublisher.Subscribe(new AdHocHandler<TcpMessage.TcpSend>(msg => TcpSends.Enqueue(msg)));

		DbConfig = CreateDbConfig();
		Db = new TFChunkDb(DbConfig);
		await Db.Open();

		Writer = new TFChunkWriter(Db);
		await Writer.Open(CancellationToken.None);

		EpochManager = new EpochManager<TStreamId>(
			Publisher,
			5,
			DbConfig.EpochCheckpoint,
			Writer,
			1, 1,
			() => new TFChunkReader(Db, Db.Config.WriterCheckpoint),
			_logFormat.RecordFactory,
			_logFormat.StreamNameIndex,
			_logFormat.EventTypeIndex,
			_logFormat.CreatePartitionManager(
				reader: new TFChunkReader(Db, Db.Config.WriterCheckpoint),
				writer: Writer),
			Guid.NewGuid());
		Service = new LeaderReplicationService(
			Publisher,
			LeaderId,
			Db,
			TcpSendPublisher,
			EpochManager,
			ClusterSize,
			false,
			new QueueStatsManager());

		Service.Handle(new SystemMessage.SystemStart());
		Service.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		await When();
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		await base.TestFixtureTearDown();
		Service.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), true, true));
	}

	public IPrepareLogRecord<TStreamId> CreateLogRecord(long eventNumber, string data = "*************") {
		var tStreamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventType = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		return _logFormat.RecordFactory.Prepare(Writer.Position, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			tStreamId, eventNumber, PrepareFlags.None, eventType, Encoding.UTF8.GetBytes(data),
			null, DateTime.UtcNow);
	}

	public async ValueTask<(Guid, TcpConnectionManager)> AddSubscription(Guid replicaId, bool isPromotable,
		Epoch[] epochs, long logPosition, CancellationToken token = default) {
		var tcpConn = new DummyTcpConnection() { ConnectionId = replicaId };

		var manager = new TcpConnectionManager(
			"Test Subscription Connection manager", TcpServiceType.External, new ClientTcpDispatcher(ESConsts.ReadRequestTimeout, 2_000),
			new SynchronousScheduler(), tcpConn, new SynchronousScheduler(),
			new InternalAuthenticationProvider(InMemoryBus.CreateTest(),
				new IODispatcher(new SynchronousScheduler(), new NoopEnvelope()),
				new StubPasswordHashAlgorithm(), 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { },
			_connectionPendingSendBytesThreshold, _connectionQueueSizeThreshold);
		var subRequest = new ReplicationMessage.ReplicaSubscriptionRequest(
			Guid.NewGuid(),
			new NoopEnvelope(),
			manager,
			ReplicationSubscriptionVersions.V_CURRENT,
			logPosition,
			Guid.NewGuid(),
			epochs,
			PortsHelper.GetLoopback(),
			LeaderId,
			replicaId,
			isPromotable);
		await Service.As<IAsyncHandle<ReplicationMessage.ReplicaSubscriptionRequest>>()
			.HandleAsync(subRequest, token);
		return (tcpConn.ConnectionId, manager);
	}

	public abstract Task When(CancellationToken token = default);

	public TcpMessage.TcpSend[] GetTcpSendsFor(TcpConnectionManager connection) {
		var sentMessages = new List<TcpMessage.TcpSend>();
		while (TcpSends.TryDequeue(out var msg)) {
			if (msg.ConnectionManager == connection)
				sentMessages.Add(msg);
		}

		return sentMessages.ToArray();
	}

	private TFChunkDbConfig CreateDbConfig() {
		ICheckpoint writerChk = new InMemoryCheckpoint(Checkpoint.Writer);
		ICheckpoint chaserChk = new InMemoryCheckpoint(Checkpoint.Chaser);
		ICheckpoint epochChk = new InMemoryCheckpoint(Checkpoint.Epoch, initValue: -1);
		ICheckpoint proposalChk = new InMemoryCheckpoint(Checkpoint.Proposal, initValue: -1);
		ICheckpoint truncateChk = new InMemoryCheckpoint(Checkpoint.Truncate, initValue: -1);
		ICheckpoint replicationCheckpoint = new InMemoryCheckpoint(-1);
		ICheckpoint indexCheckpoint = new InMemoryCheckpoint(-1);
		ICheckpoint streamExistenceFilterCheckpoint = new InMemoryCheckpoint(-1);
		var nodeConfig = new TFChunkDbConfig(
			PathName,
			chunkSize: 1000,
			maxChunksCacheSize: 10000,
			writerChk,
			chaserChk,
			epochChk,
			proposalChk,
			truncateChk,
			replicationCheckpoint,
			indexCheckpoint,
			streamExistenceFilterCheckpoint,
			inMemDb: true);
		return nodeConfig;
	}
}
