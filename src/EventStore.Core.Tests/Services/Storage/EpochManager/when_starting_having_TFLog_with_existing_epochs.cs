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
using FluentAssertions;

namespace EventStore.Core.Tests.Services.Storage;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public sealed class when_starting_having_TFLog_with_existing_epochs<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture, IDisposable {
	private TFChunkDb _db;
	private EpochManager<TStreamId> _epochManager;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private LinkedList<EpochRecord> _cache;
	private TFChunkReader _reader;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly List<Message> _published = new List<Message>();
	private List<EpochRecord> _epochs;

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
	private async ValueTask<EpochRecord> WriteEpoch(int epochNumber, long lastPos, Guid instanceId, CancellationToken token) {
		long pos = _writer.Position;
		var epoch = new EpochRecord(pos, epochNumber, Guid.NewGuid(), lastPos, DateTime.UtcNow, instanceId);
		var rec = new SystemLogRecord(epoch.EpochPosition, epoch.TimeStamp, SystemRecordType.Epoch,
			SystemRecordSerialization.Json, epoch.AsSerialized());
		await _writer.Write(rec, token);
		await _writer.Flush(token);
		return epoch;
	}
	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		_mainBus = new(nameof(when_starting_having_TFLog_with_existing_epochs<TLogFormat, TStreamId>));
		_mainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(m => _published.Add(m)));
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_reader = new TFChunkReader(_db, _db.Config.WriterCheckpoint);
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);
		_epochs = new List<EpochRecord>();
		var lastPos = 0L;
		for (int i = 0; i < 30; i++) {
			var epoch = await WriteEpoch(GetNextEpoch(), lastPos, _instanceId, CancellationToken.None);
			_epochs.Add(epoch);
			lastPos = epoch.EpochPosition;
		}
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		this.Dispose();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task starting_epoch_manager_loads_epochs() {

		_epochManager = GetManager();
		await _epochManager.Init(CancellationToken.None);
		_cache = GetCache(_epochManager);
		Assert.NotNull(_cache);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[20].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[29].EpochNumber);

	}
	[Test]
	public async Task starting_epoch_manager_with_cache_larger_than_epoch_count_loads_all_epochs() {

		_epochManager = new EpochManager<TStreamId>(_mainBus,
			1000,
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
		await _epochManager.Init(CancellationToken.None);
		_cache = GetCache(_epochManager);
		Assert.NotNull(_cache);

		Assert.That(_cache.Count == 30);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[0].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[29].EpochNumber);

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
