using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.EpochManager {
	public abstract class
		when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
		private TFChunkDb _db;
		private EpochManager<TStreamId> _epochManager;
		private LogFormatAbstractor<TStreamId> _logFormat;
		private TFChunkWriter _writer;
		private IBus _mainBus;
		private List<EpochRecord> _epochs;
		private readonly Guid _instanceId = Guid.NewGuid();
		private readonly int _numEpochs;
		private const int CachedEpochCount = 10;

		private EpochRecord WriteEpoch(int epochNumber, long lastPos, Guid instanceId) {
			long pos = _writer.Position;
			var epoch = new EpochRecord(pos, epochNumber, Guid.NewGuid(), lastPos, DateTime.UtcNow, instanceId);
			var rec = _logFormat.RecordFactory.CreateEpoch(epoch);
			_writer.Write(rec, out _);
			_writer.Flush();
			return epoch;
		}

		public when_truncating_the_epoch_checkpoint(int numEpochs) {
			_numEpochs = numEpochs;
		}

		[SetUp]
		public void SetUp() {
			_mainBus = new InMemoryBus(nameof(when_having_an_epoch_manager_and_empty_tf_log<TLogFormat, TStreamId>));

			var indexDirectory = GetFilePathFor("index");
			_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
				IndexDirectory = indexDirectory,
			});

			_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
			_db.Open();
			_writer = new TFChunkWriter(_db);
			_epochManager = new EpochManager<TStreamId>(_mainBus,
				CachedEpochCount,
				_db.Config.EpochCheckpoint,
				_writer,
				initialReaderCount: 1,
				maxReaderCount: 5,
				readerFactory: () => new TFChunkReader(_db, _db.Config.WriterCheckpoint,
					optimizeReadSideCache: _db.Config.OptimizeReadSideCache),
				_logFormat.RecordFactory,
				_logFormat.StreamNameIndex,
				_logFormat.EventTypeIndex,
				_logFormat.CreatePartitionManager(
					reader: new TFChunkReader(_db, _db.Config.WriterCheckpoint),
					writer: _writer),
				_instanceId);

			_epochManager.Init();
			_epochs = new List<EpochRecord>();

			var lastPos = 0L;
			for (int i = 0; i < _numEpochs; i++) {
				var epoch = WriteEpoch(i, lastPos, _instanceId);
				_epochManager.AddEpochToCache(epoch);
				_epochs.Add(epoch);
				lastPos = epoch.EpochPosition;
			}
		}


		[TearDown]
		public void TearDown() {
			try {
				_logFormat?.Dispose();
				_writer?.Dispose();
			} catch {
				//workaround for TearDown error
			}

			_db?.Dispose();
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class with_no_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
			public with_no_epochs() : base(0) { }

			[Test]
			public void cannot_truncate_before_position_zero() {
				Assert.False(_epochManager.TryTruncateBefore(0, out _));
			}

			[Test]
			public void cannot_truncate_before_arbitrary_position() {
				Assert.False(_epochManager.TryTruncateBefore(12, out _));
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class with_two_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
			public with_two_epochs() : base(2) { }

			[Test]
			public void cannot_truncate_before_first_epoch_position() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition, out _));
			}

			[Test]
			public void can_truncate_before_first_epoch_position_plus_one() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out var epoch));
				Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
			}

			[Test]
			public void can_truncate_before_second_epoch_position() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[1].EpochPosition, out var epoch));
				Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
			}

			[Test]
			public void can_truncate_before_second_epoch_position_plus_one() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[1].EpochPosition + 1, out var epoch));
				Assert.AreEqual(_epochs[1].EpochNumber, epoch.EpochNumber);
			}

			[Test]
			public void checkpoint_is_changed_after_truncation() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out _));
				Assert.AreEqual(_epochs[0].EpochPosition, _db.Config.EpochCheckpoint.Read());
			}

			[Test]
			public void checkpoint_is_unchanged_after_no_truncation() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition, out _));
				Assert.AreEqual(_epochs[1].EpochPosition, _db.Config.EpochCheckpoint.Read());
			}

			[Test]
			public void cannot_read_checkpoint_even_if_no_truncation() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition, out _));
				Assert.Throws<InvalidOperationException>(() => {
					_epochManager.Init(); // triggers a checkpoint read internally
				});
			}

			[Test]
			public void cannot_read_checkpoint_after_truncation() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out _));
				Assert.Throws<InvalidOperationException>(() => {
					_epochManager.Init(); // triggers a checkpoint read internally
				});
			}

			[Test]
			public void cannot_write_checkpoint_even_if_no_truncation() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition, out _));
				Assert.Throws<InvalidOperationException>(() => {
					_epochManager.WriteNewEpoch(2); // triggers a checkpoint write internally
				});
			}

			[Test]
			public void cannot_write_checkpoint_after_truncation() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out _));
				Assert.Throws<InvalidOperationException>(() => {
					_epochManager.WriteNewEpoch(2); // triggers a checkpoint write internally
				});
			}

			[Test]
			public void cannot_truncate_twice() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out _));
				Assert.Throws<InvalidOperationException>(() => {
					_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out _);
				});
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class with_cache_size_minus_three_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
			public with_cache_size_minus_three_epochs() : base(CachedEpochCount - 3) { }

			[Test]
			public void cannot_truncate_before_first_epoch_position() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition, out _));
			}

			[Test]
			public void can_truncate_before_first_epoch_position_plus_one() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out var epoch));
				Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class with_cache_size_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
			public with_cache_size_epochs() : base(CachedEpochCount) { }
			[Test]
			public void cannot_truncate_before_first_epoch_position() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition, out _));
			}

			[Test]
			public void can_truncate_before_first_epoch_position_plus_one() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[0].EpochPosition + 1, out var epoch));
				Assert.AreEqual(_epochs[0].EpochNumber, epoch.EpochNumber);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class with_cache_size_plus_two_epochs : when_truncating_the_epoch_checkpoint<TLogFormat, TStreamId> {
			public with_cache_size_plus_two_epochs() : base(CachedEpochCount + 2) { }

			[Test]
			public void cannot_truncate_before_third_epoch_position() {
				Assert.False(_epochManager.TryTruncateBefore(_epochs[2].EpochPosition, out _));
			}

			[Test]
			public void can_truncate_before_third_epoch_position_plus_one() {
				Assert.True(_epochManager.TryTruncateBefore(_epochs[2].EpochPosition + 1, out var epoch));
				Assert.AreEqual(_epochs[2].EpochNumber, epoch.EpochNumber);
			}
		}
	}
}
