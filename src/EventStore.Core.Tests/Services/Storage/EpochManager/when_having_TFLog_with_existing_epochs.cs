// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.LogAbstraction;
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
using EventStore.Common.Utils;
using Newtonsoft.Json.Linq;
using EventStore.Core.LogV3;

namespace EventStore.Core.Tests.Services.Storage;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_having_TFLog_with_existing_epochs<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture, IDisposable {
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

	private async ValueTask<EpochRecord> WriteEpoch(int epochNumber, long lastPos, Guid instanceId, CancellationToken token) {
		long pos = _writer.Position;
		var epoch = new EpochRecord(pos, epochNumber, Guid.NewGuid(), lastPos, DateTime.UtcNow, instanceId);
		var rec = _logFormat.RecordFactory.CreateEpoch(epoch);
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
		Assert.That(_cache.Count == 0);
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
	// epoch manager is stateful with TFLog,
	// and TFLog is expesive to build fresh for each test
	// and the tests depend on previous state in the epoch manager
	// so this test will run through the test cases
	// in order
	[Test]
	public async Task can_add_epochs_to_cache() {

		Assert.That(_cache.Count == 0);
		//add fist epoch to empty cache
		await _epochManager.AddEpochToCache(_epochs[3], CancellationToken.None);

		Assert.That(_cache.Count is 4);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[0].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[3].EpochNumber);

		//add new last epoch
		await _epochManager.AddEpochToCache(_epochs[4], CancellationToken.None);

		Assert.That(_cache.Count is 5);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[0].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[4].EpochNumber);

		//idempotent add
		await _epochManager.AddEpochToCache(_epochs[1], CancellationToken.None);
		await _epochManager.AddEpochToCache(_epochs[2], CancellationToken.None);
		await _epochManager.AddEpochToCache(_epochs[3], CancellationToken.None);

		Assert.That(_cache.Count == 5);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[0].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[4].EpochNumber);

		//add new skip 1 last epoch
		await _epochManager.AddEpochToCache(_epochs[6], CancellationToken.None);

		Assert.That(_cache.Count == 7);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[0].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[6].EpochNumber);

		//add new skip 5 last epoch
		await _epochManager.AddEpochToCache(_epochs[11], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[2].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[11].EpochNumber);

		//add last rolls cache
		await _epochManager.AddEpochToCache(_epochs[12], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[3].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[12].EpochNumber);


		//add epoch before cache
		await _epochManager.AddEpochToCache(_epochs[1], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[3].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[12].EpochNumber);

		//add idempotent first epoch
		await _epochManager.AddEpochToCache(_epochs[2], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[3].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[12].EpochNumber);

		//add idempotent last epoch
		await _epochManager.AddEpochToCache(_epochs[12], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[3].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[12].EpochNumber);

		//add disjunct skip epoch
		await _epochManager.AddEpochToCache(_epochs[24], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);


		//cannot get epoch ahead of last cached on master
		var nextEpoch = await _epochManager.GetEpochAfter(_epochs[24].EpochNumber, false, CancellationToken.None);
		Assert.Null(nextEpoch);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);

		//cannot get epoch ahead of cache on master
		nextEpoch = await _epochManager.GetEpochAfter(_epochs[25].EpochNumber, false, CancellationToken.None);
		Assert.Null(nextEpoch);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);

		//can get next  in cache
		nextEpoch = await _epochManager.GetEpochAfter(_epochs[20].EpochNumber, false, CancellationToken.None);

		Assert.That(nextEpoch.EpochPosition == _epochs[21].EpochPosition);
		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);

		//can get next from first
		nextEpoch = await _epochManager.GetEpochAfter(_epochs[15].EpochNumber, false, CancellationToken.None);

		Assert.That(nextEpoch.EpochPosition == _epochs[16].EpochPosition);
		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);

		//can get next epoch from just before cache
		nextEpoch = await _epochManager.GetEpochAfter(_epochs[14].EpochNumber, false, CancellationToken.None);

		Assert.That(nextEpoch.EpochPosition == _epochs[15].EpochPosition);
		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);

		//can get next epoch from before cache
		nextEpoch = await _epochManager.GetEpochAfter(_epochs[10].EpochNumber, false, CancellationToken.None);

		Assert.That(nextEpoch.EpochPosition == _epochs[11].EpochPosition);
		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);

		//can get next epoch from 0 epoch
		nextEpoch = await _epochManager.GetEpochAfter(_epochs[0].EpochNumber, false, CancellationToken.None);

		Assert.That(nextEpoch.EpochPosition == _epochs[1].EpochPosition);
		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[15].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[24].EpochNumber);


		//can add last epoch in log
		await _epochManager.AddEpochToCache(_epochs[29], CancellationToken.None);

		Assert.That(_cache.Count == 10);
		Assert.That(_cache.First.Value.EpochNumber == _epochs[20].EpochNumber);
		Assert.That(_cache.Last.Value.EpochNumber == _epochs[29].EpochNumber);

		// can write an epoch with epoch information (even though previous epochs
		// dont have epoch information)
		await _epochManager.WriteNewEpoch(GetNextEpoch(), CancellationToken.None);
		await _epochManager.WriteNewEpoch(GetNextEpoch(), CancellationToken.None);
		var epochsWritten = _published.OfType<SystemMessage.EpochWritten>().ToArray();
		Assert.AreEqual(2, epochsWritten.Length);
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
	}

	public class EpochDto {
		public Guid LeaderInstanceId { get; set; }
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
