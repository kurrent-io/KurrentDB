// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Index;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage;

[TestFixture]
public abstract class WriteEventsToIndexScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected SynchronousScheduler _publisher;
	protected ITransactionFileReader _tfReader;
	protected ITableIndex<TStreamId> _tableIndex;
	protected IIndexBackend<TStreamId> _indexBackend;
	protected IValueLookup<TStreamId> _streamIds;
	protected INameLookup<TStreamId> _streamNames;
	protected ISystemStreamLookup<TStreamId> _systemStreams;
	protected IStreamNamesProvider<TStreamId> _provider;
	protected IValidator<TStreamId> _validator;
	protected ISizer<TStreamId> _sizer;
	protected IIndexReader<TStreamId> _indexReader;
	protected IIndexWriter<TStreamId> _indexWriter;
	protected IIndexCommitter<TStreamId> _indexCommitter;
	protected ObjectPool<ITransactionFileReader> _readerPool;
	protected LogFormatAbstractor<TStreamId> _logFormat;
	protected const int RecordOffset = 1000;
	public IReadOnlyList<IPrepareLogRecord<TStreamId>> CreatePrepareLogRecord(TStreamId streamId, int expectedVersion, TStreamId eventType, Guid eventId, long transactionPosition) {
		return new[]{
			PrepareLogRecord.SingleWrite (
				_logFormat.RecordFactory,
				transactionPosition,
				Guid.NewGuid(),
				eventId,
				streamId,
				expectedVersion,
				eventType,
				new byte[0],
				new byte[0],
				DateTime.Now,
				PrepareFlags.IsCommitted
			)
		};
	}

	public IReadOnlyList<IPrepareLogRecord<TStreamId>> CreatePrepareLogRecords(TStreamId streamId, int expectedVersion, IList<TStreamId> eventTypes, IList<Guid> eventIds, long transactionPosition) {
		if (eventIds.Count != eventTypes.Count)
			throw new Exception("eventType and eventIds length mismatch!");
		if (eventIds.Count == 0)
			throw new Exception("eventIds is empty");
		if (eventIds.Count == 1)
			return CreatePrepareLogRecord(streamId, expectedVersion, eventTypes[0], eventIds[0], transactionPosition);

		var numEvents = eventTypes.Count;
		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;

		var prepares = new List<IPrepareLogRecord<TStreamId>>();
		for (var i = 0; i < numEvents; i++) {
			PrepareFlags flags = PrepareFlags.Data | PrepareFlags.IsCommitted;
			if (i == 0)
				flags |= PrepareFlags.TransactionBegin;
			if (i == numEvents - 1)
				flags |= PrepareFlags.TransactionEnd;

			prepares.Add(
				PrepareLogRecord.Prepare(
					recordFactory,
					transactionPosition + RecordOffset * i,
					Guid.NewGuid(),
					eventIds[i],
					transactionPosition,
					i,
					streamId,
					expectedVersion + i,
					flags,
					eventTypes[i],
					new byte[0],
					new byte[0],
					DateTime.Now
			));
		}

		return prepares;
	}

	public CommitLogRecord CreateCommitLogRecord(long logPosition, long transactionPosition, long firstEventNumber) {
		return new CommitLogRecord(logPosition, Guid.NewGuid(), transactionPosition, DateTime.Now, 0);
	}

	public void WriteToDB(IEnumerable<IPrepareLogRecord<TStreamId>> prepares) {
		foreach (var prepare in prepares) {
			((FakeInMemoryTfReader)_tfReader).AddRecord(prepare, prepare.LogPosition);
		}
	}

	public void WriteToDB(CommitLogRecord commit) {
		((FakeInMemoryTfReader)_tfReader).AddRecord(commit, commit.LogPosition);
	}

	public void PreCommitToIndex(IEnumerable<IPrepareLogRecord<TStreamId>> prepares) {
		var preparesArray = prepares.ToArray();
		_indexWriter.PreCommit(
			committedPrepares: preparesArray,
			eventStreamIndexes: null);
	}

	public ValueTask PreCommitToIndex(CommitLogRecord commitLogRecord, CancellationToken token) {
		return _indexWriter.PreCommit(commitLogRecord, token);
	}

	public async ValueTask CommitToIndex(IReadOnlyList<IPrepareLogRecord<TStreamId>> prepares, CancellationToken token) {
		await _indexCommitter.Commit(
			prepares,
			numStreams: 1,
			eventStreamIndexes: null,
			isTfEof: false,
			cacheLastEventNumber: false,
			token);
	}

	public async ValueTask CommitToIndex(CommitLogRecord commitLogRecord, CancellationToken token) {
		await _indexCommitter.Commit(commitLogRecord, false, false, token);
	}

	public abstract ValueTask WriteEvents(CancellationToken token);

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
			IndexDirectory = GetFilePathFor("index"),
		});
		_provider = _logFormat.StreamNamesProvider;
		_publisher = new("publisher");
		_tfReader = new FakeInMemoryTfReader(RecordOffset);
		_tableIndex = new FakeInMemoryTableIndex<TStreamId>();
		_provider.SetTableIndex(_tableIndex);
		_readerPool = new ObjectPool<ITransactionFileReader>(
			"ReadIndex readers pool", 5, 100,
			() => _tfReader);
		_indexBackend = new IndexBackend<TStreamId>(_readerPool,
			new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>("LastEventNumber", 100_000),
			new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>("StreamMetadata", 100_000));
		_streamIds = _logFormat.StreamIds;
		_validator = _logFormat.StreamIdValidator;
		var emptyStreamId = _logFormat.EmptyStreamId;
		_sizer = _logFormat.StreamIdSizer;
		_indexReader = new IndexReader<TStreamId>(_indexBackend, _tableIndex, _provider, _validator,
			_logFormat.StreamExistenceFilterReader, new StreamMetadata(maxCount: 100000), 100, false);
		_streamNames = _logFormat.StreamNames;
		_systemStreams = _logFormat.SystemStreams;
		_indexWriter = new IndexWriter<TStreamId>(_indexBackend, _indexReader, _streamIds, _streamNames,
			_systemStreams, emptyStreamId, _sizer);
		_indexCommitter = new IndexCommitter<TStreamId>(_publisher, _indexBackend, _indexReader, _tableIndex,
			_logFormat.StreamNameIndexConfirmer, _streamNames, _logFormat.EventTypeIndexConfirmer, _logFormat.EventTypes,
			_systemStreams, _logFormat.StreamExistenceFilter, _logFormat.StreamExistenceFilterInitializer, new InMemoryCheckpoint(-1), new IndexStatusTracker.NoOp(), new IndexTracker.NoOp(), false);

		await WriteEvents(CancellationToken.None);
	}

	public override Task TestFixtureTearDown() {
		_logFormat?.Dispose();
		_readerPool.Dispose();
		return base.TestFixtureTearDown();
	}
}
