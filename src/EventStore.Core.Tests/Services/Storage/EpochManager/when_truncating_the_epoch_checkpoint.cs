// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Tests.Services.Storage.EpochManager;

public abstract class
	when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private TFChunkDb _db;
	private EpochManager<TStreamId> _epochManager;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private List<EpochRecord> _epochs;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly int _numEpochs;
	private const int CachedEpochCount = 10;

	private async ValueTask<EpochRecord> WriteEpoch(int epochNumber, long lastPos, Guid instanceId, CancellationToken token) {
		long pos = _writer.Position;
		var epoch = new EpochRecord(pos, epochNumber, Guid.NewGuid(), lastPos, DateTime.UtcNow, instanceId);
		var rec = _logFormat.RecordFactory.CreateEpoch(epoch);
		await _writer.Write(rec, token);
		await _writer.Flush(token);
		return epoch;
	}

	public when_truncating_the_epoch_checkpoint(int numEpochs) {
		_numEpochs = numEpochs;
	}

	[SetUp]
	public async Task SetUp() {
		_mainBus = new(nameof(when_having_an_epoch_manager_and_empty_tf_log<TLogFormat, TStreamId>));

		var indexDirectory = GetFilePathFor("index");
		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});

		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);
		_epochManager = new EpochManager<TStreamId>(_mainBus,
			CachedEpochCount,
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
		_epochs = new List<EpochRecord>();

		var lastPos = 0L;
		for (int i = 0; i < _numEpochs; i++) {
			var epoch = await WriteEpoch(i, lastPos, _instanceId, CancellationToken.None);
			await _epochManager.AddEpochToCache(epoch, CancellationToken.None);
			_epochs.Add(epoch);
			lastPos = epoch.EpochPosition;
		}
	}


	[TearDown]
	public async Task TearDown() {
		try {
			_logFormat?.Dispose();
			await (_writer?.DisposeAsync() ?? ValueTask.CompletedTask);
		} catch {
			//workaround for TearDown error
		}

		await (_db?.DisposeAsync() ?? ValueTask.CompletedTask);
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_no_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
		public with_no_epochs() : base(0) { }

		[Test]
		public async Task cannot_truncate_before_position_zero() {
			Assert.Null(await _epochManager.TryTruncateBefore(0, CancellationToken.None));
		}

		[Test]
		public async Task cannot_truncate_before_arbitrary_position() {
			Assert.Null(await _epochManager.TryTruncateBefore(12, CancellationToken.None));
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_two_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
		public with_two_epochs() : base(2) { }

		[Test]
		public async Task cannot_truncate_before_first_epoch_position() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition, CancellationToken.None));
		}

		[Test]
		public async Task can_truncate_before_first_epoch_position_plus_one() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
		}

		[Test]
		public async Task can_truncate_before_second_epoch_position() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[1].EpochPosition, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
		}

		[Test]
		public async Task can_truncate_before_second_epoch_position_plus_one() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[1].EpochPosition + 1, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[1].EpochNumber, epoch.EpochNumber);
		}

		[Test]
		public async Task checkpoint_is_changed_after_truncation() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[0].EpochPosition, _db.Config.EpochCheckpoint.Read());
		}

		[Test]
		public async Task checkpoint_is_unchanged_after_no_truncation() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition, CancellationToken.None));
			Assert.AreEqual(_epochs[1].EpochPosition, _db.Config.EpochCheckpoint.Read());
		}

		[Test]
		public async Task cannot_read_checkpoint_even_if_no_truncation() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition, CancellationToken.None));
			Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await _epochManager.Init(CancellationToken.None); // triggers a checkpoint read internally
			});
		}

		[Test]
		public async Task cannot_read_checkpoint_after_truncation() {
			Assert.NotNull(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None));
			Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await _epochManager.Init(CancellationToken.None); // triggers a checkpoint read internally
			});
		}

		[Test]
		public async Task cannot_write_checkpoint_even_if_no_truncation() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition, CancellationToken.None));
			Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await _epochManager.WriteNewEpoch(2, CancellationToken.None); // triggers a checkpoint write internally
			});
		}

		[Test]
		public async Task cannot_write_checkpoint_after_truncation() {
			Assert.NotNull(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None));
			Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await _epochManager.WriteNewEpoch(2, CancellationToken.None); // triggers a checkpoint write internally
			});
		}

		[Test]
		public async Task cannot_truncate_twice() {
			Assert.NotNull(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None));
			Assert.ThrowsAsync<InvalidOperationException>(async () => {
				await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None);
			});
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_cache_size_minus_three_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
		public with_cache_size_minus_three_epochs() : base(CachedEpochCount - 3) { }

		[Test]
		public async Task cannot_truncate_before_first_epoch_position() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition, CancellationToken.None));
		}

		[Test]
		public async Task can_truncate_before_first_epoch_position_plus_one() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_cache_size_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
		public with_cache_size_epochs() : base(CachedEpochCount) { }
		[Test]
		public async Task cannot_truncate_before_first_epoch_position() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition, CancellationToken.None));
		}

		[Test]
		public async Task can_truncate_before_first_epoch_position_plus_one() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_cache_size_plus_two_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
		public with_cache_size_plus_two_epochs() : base(CachedEpochCount + 2) { }

		[Test]
		public async Task cannot_truncate_before_third_epoch_position() {
			Assert.Null(await _epochManager.TryTruncateBefore(_epochs[2].EpochPosition, CancellationToken.None));
		}

		[Test]
		public async Task can_truncate_before_third_epoch_position_plus_one() {
			var epoch = await _epochManager.TryTruncateBefore(_epochs[2].EpochPosition + 1, CancellationToken.None);
			Assert.NotNull(epoch);
			Assert.AreEqual(_epochs[2].EpochNumber, epoch.EpochNumber);
		}
	}
}
