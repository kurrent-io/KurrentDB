// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Bus;
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
using EventStore.Core.Services;
using EventStore.Core.Data;
using EventStore.Common.Utils;
using EventStore.Core.LogAbstraction;
using EventStore.Core.LogV3;

namespace EventStore.Core.Tests.Services.Storage;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_having_an_epoch_manager_and_empty_tf_log<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private TFChunkDb _db;
	private EpochManager<TStreamId> _epochManager;
	private LogFormatAbstractor<TStreamId> _logFormat;
	private LinkedList<EpochRecord> _cache;
	private TFChunkReader _reader;
	private TFChunkWriter _writer;
	private SynchronousScheduler _mainBus;
	private readonly Guid _instanceId = Guid.NewGuid();
	private readonly List<Message> _published = new();

	private int GetNextEpoch() {
		return (int)Interlocked.Increment(ref _currentEpoch);
	}
	private long _currentEpoch = -1;
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

		_mainBus = new(nameof(when_having_an_epoch_manager_and_empty_tf_log<TLogFormat, TStreamId>));
		_mainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(m => _published.Add(m)));
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		await _db.Open();
		_reader = new TFChunkReader(_db, _db.Config.WriterCheckpoint);
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);

		_epochManager = GetManager();
		await _epochManager.Init(CancellationToken.None);
		_cache = GetCache(_epochManager);
		Assert.NotNull(_cache);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		await (_writer?.DisposeAsync() ?? ValueTask.CompletedTask);
		await (_db?.DisposeAsync() ?? ValueTask.CompletedTask);
		await base.TestFixtureTearDown();
	}

	// epoch manager is stateful with TFLog,
	// and TFLog is expesive to build fresh for each test
	// and the tests depend on previous state in the epoch manager
	// so this test will run through the test cases
	// in order
	[Test]
	public async Task can_write_epochs() {

		//can write first epoch
		_published.Clear();
		var beforeWrite = DateTime.UtcNow;
		await _epochManager.WriteNewEpoch(GetNextEpoch(), CancellationToken.None);
		Assert.That(_published.Count == 1);
		var epochWritten = _published[0] as SystemMessage.EpochWritten;
		Assert.NotNull(epochWritten);
		Assert.That(epochWritten.Epoch.EpochNumber == 0);
		Assert.That(epochWritten.Epoch.PrevEpochPosition == -1);
		Assert.That(epochWritten.Epoch.EpochPosition == 0);
		Assert.That(epochWritten.Epoch.LeaderInstanceId == _instanceId);
		Assert.That(epochWritten.Epoch.TimeStamp < DateTime.UtcNow);
		Assert.That(epochWritten.Epoch.TimeStamp >= beforeWrite);

		// will_cache_epochs_written() {

		for (int i = 0; i < 4; i++) {
			await _epochManager.WriteNewEpoch(GetNextEpoch(), CancellationToken.None);
		}
		Assert.That(_cache.Count == 5);
		Assert.That(_cache.First.Value.EpochNumber == 0);
		Assert.That(_cache.Last.Value.EpochNumber == 4);
		var epochs = new List<int>();
		var epoch = _cache.First;
		while (epoch != null) {
			epochs.Add(epoch.Value.EpochNumber);
			epoch = epoch.Next;
		}
		CollectionAssert.IsOrdered(epochs);

		// can_write_more_epochs_than_cache_size

		for (int i = 0; i < 16; i++) {
			await _epochManager.WriteNewEpoch(GetNextEpoch(), CancellationToken.None);
		}
		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == 11);
		Assert.That(_cache.Last.Value.EpochNumber == 20);
		epochs = new List<int>();
		epoch = _cache.First;
		while (epoch != null) {
			epochs.Add(epoch.Value.EpochNumber);
			epoch = epoch.Next;
		}
		CollectionAssert.IsOrdered(epochs);

		// has written epoch information
		var epochsWritten = _published.OfType<SystemMessage.EpochWritten>().ToArray();
		Assert.AreEqual(1 + 4 + 16, epochsWritten.Length);
		for (int i = 0; i < epochsWritten.Length; i++) {
			_reader.Reposition(epochsWritten[i].Epoch.EpochPosition);
			await _reader.TryReadNext(CancellationToken.None); // read epoch
			IPrepareLogRecord<TStreamId> epochInfo;
			while (true) {
				var result = await _reader.TryReadNext(CancellationToken.None);
				Assert.True(result.Success);
				if (result.LogRecord is IPrepareLogRecord<TStreamId> prepare) {
					epochInfo = prepare;
					break;
				}
			}
			var expectedStreamId = LogFormatHelper<TLogFormat, TStreamId>.Choose<TStreamId>(
				SystemStreams.EpochInformationStream,
				LogV3SystemStreams.EpochInformationStreamNumber);
			var expectedEventType = LogFormatHelper<TLogFormat, TStreamId>.Choose<TStreamId>(
				SystemEventTypes.EpochInformation,
				LogV3SystemEventTypes.EpochInformationNumber);
			Assert.AreEqual(expectedStreamId, epochInfo.EventStreamId);
			Assert.AreEqual(expectedEventType, epochInfo.EventType);
			Assert.AreEqual(i - 1, epochInfo.ExpectedVersion);
			Assert.AreEqual(_instanceId, epochInfo.Data.ParseJson<EpochDto>().LeaderInstanceId);
		}
		_published.Clear();
	}

	public  class EpochDto {
		public Guid LeaderInstanceId { get; set; }
	}
}
