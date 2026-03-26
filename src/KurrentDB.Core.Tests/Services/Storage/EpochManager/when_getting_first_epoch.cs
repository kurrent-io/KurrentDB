// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.EpochManager;
using KurrentDB.Core.Tests.TransactionLog;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.EpochManager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public sealed class when_getting_first_epoch_from_empty_log<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture, IDisposable {
	private TFChunkDb _db;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly List<Message> _published = new();

	private EpochManager<TStreamId> GetManager(int cacheSize = 10) {
		return new EpochManager<TStreamId>(_mainBus,
			cacheSize,
			_db.Config.EpochCheckpoint,
			_writer,
			initialReaderCount: 1,
			maxReaderCount: 5,
			new TFChunkReader(_db, _db.Config.WriterCheckpoint),
			_logFormat.RecordFactory,
			_logFormat.StreamNameIndex,
			_logFormat.EventTypeIndex,
			_logFormat.CreatePartitionManager(
				reader: new TFChunkReader(_db, _db.Config.WriterCheckpoint),
				writer: _writer),
			_instanceId);
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		_mainBus = new(nameof(when_getting_first_epoch_from_empty_log<TLogFormat, TStreamId>));
		_mainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(m => _published.Add(m)));
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		Dispose();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task first_epoch_lifecycle() {
		var manager = GetManager();
		await manager.Init(CancellationToken.None);

		// null before any epochs exist
		Assert.That(manager.GetFirstEpoch(), Is.Null);

		// available after writing epoch 0
		await manager.WriteNewEpoch(0, CancellationToken.None);
		var firstEpoch = manager.GetFirstEpoch();
		Assert.That(firstEpoch, Is.Not.Null);
		Assert.That(firstEpoch.EpochNumber, Is.EqualTo(0));
		Assert.That(firstEpoch.EpochPosition, Is.EqualTo(0));
		Assert.That(firstEpoch.PrevEpochPosition, Is.EqualTo(-1));

		// still available after writing more epochs
		for (int i = 1; i <= 20; i++)
			await manager.WriteNewEpoch(i, CancellationToken.None);

		Assert.That(manager.GetFirstEpoch().EpochId, Is.EqualTo(firstEpoch.EpochId));
	}

	public void Dispose() {
		try {
			_logFormat?.Dispose();
			using var task = _writer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
			task.Wait();
		} catch {
			// workaround for TearDown error
		}

		using (var task = _db?.DisposeAsync().AsTask() ?? Task.CompletedTask) {
			task.Wait();
		}
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public sealed class when_getting_first_epoch_after_caching<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture, IDisposable {
	private TFChunkDb _db;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly List<Message> _published = new();

	private EpochManager<TStreamId> GetManager(int cacheSize = 10) {
		return new EpochManager<TStreamId>(_mainBus,
			cacheSize,
			_db.Config.EpochCheckpoint,
			_writer,
			initialReaderCount: 1,
			maxReaderCount: 5,
			new TFChunkReader(_db, _db.Config.WriterCheckpoint),
			_logFormat.RecordFactory,
			_logFormat.StreamNameIndex,
			_logFormat.EventTypeIndex,
			_logFormat.CreatePartitionManager(
				reader: new TFChunkReader(_db, _db.Config.WriterCheckpoint),
				writer: _writer),
			_instanceId);
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		_mainBus = new(nameof(when_getting_first_epoch_after_caching<TLogFormat, TStreamId>));
		_mainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(m => _published.Add(m)));
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		Dispose();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task is_set_after_caching_epoch_zero() {
		var manager = GetManager();
		await manager.Init(CancellationToken.None);

		var epoch0 = new EpochRecord(0, 0, Guid.NewGuid(), -1, DateTime.UtcNow, _instanceId);
		await manager.CacheEpoch(epoch0, CancellationToken.None);

		var firstEpoch = manager.GetFirstEpoch();
		Assert.That(firstEpoch, Is.Not.Null);
		Assert.That(firstEpoch.EpochId, Is.EqualTo(epoch0.EpochId));
	}

	public void Dispose() {
		try {
			_logFormat?.Dispose();
			using var task = _writer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
			task.Wait();
		} catch {
			// workaround for TearDown error
		}

		using (var task = _db?.DisposeAsync().AsTask() ?? Task.CompletedTask) {
			task.Wait();
		}
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public sealed class when_getting_first_epoch_with_existing_epochs<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture, IDisposable {
	private TFChunkDb _db;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly List<Message> _published = new();
	private List<EpochRecord> _epochs;

	private EpochManager<TStreamId> GetManager(int cacheSize = 10) {
		return new EpochManager<TStreamId>(_mainBus,
			cacheSize,
			_db.Config.EpochCheckpoint,
			_writer,
			initialReaderCount: 1,
			maxReaderCount: 5,
			new TFChunkReader(_db, _db.Config.WriterCheckpoint),
			_logFormat.RecordFactory,
			_logFormat.StreamNameIndex,
			_logFormat.EventTypeIndex,
			_logFormat.CreatePartitionManager(
				reader: new TFChunkReader(_db, _db.Config.WriterCheckpoint),
				writer: _writer),
			_instanceId);
	}

	private async ValueTask<EpochRecord> WriteEpochRaw(int epochNumber, long lastPos, CancellationToken token) {
		long pos = _writer.Position;
		var epoch = new EpochRecord(pos, epochNumber, Guid.NewGuid(), lastPos, DateTime.UtcNow, _instanceId);
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

		_mainBus = new(nameof(when_getting_first_epoch_with_existing_epochs<TLogFormat, TStreamId>));
		_mainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(m => _published.Add(m)));
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);

		_epochs = new List<EpochRecord>();
		var lastPos = -1L;
		for (int i = 0; i < 30; i++) {
			var epoch = await WriteEpochRaw(i, lastPos, CancellationToken.None);
			_epochs.Add(epoch);
			lastPos = epoch.EpochPosition;
		}
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		Dispose();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task is_available_when_epoch_zero_is_in_cache() {
		var manager = GetManager(cacheSize: 1000);
		await manager.Init(CancellationToken.None);

		var firstEpoch = manager.GetFirstEpoch();
		Assert.That(firstEpoch, Is.Not.Null);
		Assert.That(firstEpoch.EpochNumber, Is.EqualTo(0));
		Assert.That(firstEpoch.EpochId, Is.EqualTo(_epochs[0].EpochId));
	}

	[Test]
	public async Task is_available_when_epoch_zero_is_not_in_cache() {
		var manager = GetManager(cacheSize: 5);
		await manager.Init(CancellationToken.None);

		// cache only holds 5 epochs (25-29), but epoch 0 should still be available
		Assert.That(manager.GetLastEpoch().EpochNumber, Is.EqualTo(29));

		var firstEpoch = manager.GetFirstEpoch();
		Assert.That(firstEpoch, Is.Not.Null);
		Assert.That(firstEpoch.EpochNumber, Is.EqualTo(0));
		Assert.That(firstEpoch.EpochId, Is.EqualTo(_epochs[0].EpochId));
	}

	public void Dispose() {
		try {
			_logFormat?.Dispose();
			using var task = _writer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
			task.Wait();
		} catch {
			// workaround for TearDown error
		}

		using (var task = _db?.DisposeAsync().AsTask() ?? Task.CompletedTask) {
			task.Wait();
		}
	}
}
