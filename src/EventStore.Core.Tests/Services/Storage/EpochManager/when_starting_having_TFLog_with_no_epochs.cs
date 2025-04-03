// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.LogV2;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;
using NUnit.Framework.Internal;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using System.Threading;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.Tests.Services.Storage;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public sealed class when_starting_having_TFLog_with_no_epochs<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture, IDisposable {
	private TFChunkDb _db;
	private EpochManager<TStreamId> _epochManager;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private LinkedList<EpochRecord> _cache;
	private TFChunkReader _reader;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly List<Message> _published = new List<Message>();
	public when_starting_having_TFLog_with_no_epochs() {
	}

	private static int GetNextEpoch() {
		return (int)Interlocked.Increment(ref _currentEpoch);
	}
	private static long _currentEpoch = -1;
	private EpochManager<TStreamId> GetManager() {
		return new EpochManager<TStreamId>(_mainBus,
			10,
			_db.Config.EpochCheckpoint,
			_writer,
			initialReaderCount: 1,
			maxReaderCount: 5,
			readerFactory: () => new TFChunkReader(_db, _db.Config.WriterCheckpoint),
			_logFormat.RecordFactory,
			_logFormat.StreamNameIndex,
			_logFormat.EventTypeIndex,
			_logFormat.CreatePartitionManager(
				reader: new TFChunkReader(_db, _db.Config.WriterCheckpoint),
				writer: _writer),
			_instanceId);
	}
	private LinkedList<EpochRecord> GetCache(EpochManager<TStreamId> manager) {
		return (LinkedList<EpochRecord>)typeof(EpochManager<TStreamId>).GetField("_epochs", BindingFlags.NonPublic | BindingFlags.Instance)
			.GetValue(_epochManager);
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		_mainBus = new(nameof(when_starting_having_TFLog_with_no_epochs<TLogFormat, TStreamId>));
		_mainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(m => _published.Add(m)));
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_reader = new TFChunkReader(_db, _db.Config.WriterCheckpoint);
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		this.Dispose();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task starting_epoch_manager_loads_without_epochs() {

		_epochManager = GetManager();
		await _epochManager.Init(CancellationToken.None);
		_cache = GetCache(_epochManager);
		Assert.NotNull(_cache);

		Assert.That(_cache.Count is 0);
		Assert.That(_cache?.First?.Value is null);
		Assert.That(_cache?.Last?.Value is null);
		Assert.That(_epochManager.LastEpochNumber == -1);
		await _epochManager.WriteNewEpoch(0, CancellationToken.None);
		Assert.That(_cache.Count is 1);
		Assert.That(_cache.First.Value.EpochNumber is 0);
		Assert.That(_cache.Last.Value.EpochNumber is 0);
		Assert.That(_epochManager.LastEpochNumber is 0);

	}

	public void Dispose() {
		//epochManager?.Dispose();
		//reader?.Dispose();
		try {
			_logFormat?.Dispose();
			using var task = _writer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
			task.Wait();
		} catch {
			//workaround for TearDown error
		}

		using (var task = _db?.DisposeAsync().AsTask() ?? Task.CompletedTask) {
			task.Wait();
		}
	}
}
