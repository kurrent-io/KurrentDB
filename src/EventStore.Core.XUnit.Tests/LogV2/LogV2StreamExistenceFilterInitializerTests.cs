// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogV2;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV2;

public class LogV2StreamExistenceFilterInitializerTests : DirectoryPerTest<LogV2StreamExistenceFilterInitializerTests> {
	private readonly TableIndex<string> _tableIndex;
	private readonly LogV2StreamExistenceFilterInitializer _sut;
	private readonly FakeInMemoryTfReader _log;
	private readonly MockExistenceFilter _filter;
	private readonly int _recordOffset = 100;
	private long _nextLogPosition = 0;

	public LogV2StreamExistenceFilterInitializerTests() {
		_log = new FakeInMemoryTfReader(recordOffset: _recordOffset);
		_tableIndex = new TableIndex<string>(
			directory: Fixture.Directory,
			lowHasher: new XXHashUnsafe(),
			highHasher: new Murmur3AUnsafe(),
			emptyStreamId: string.Empty,
			memTableFactory: () => new HashListMemTable(
				version: PTableVersions.IndexV4,
				maxSize: 1_000_000 * 2),
			maxSizeForMemory: 100_000,
			tfReaderFactory: () => new TFReaderLease(_log),
			ptableVersion: PTableVersions.IndexV4,
			maxAutoMergeIndexLevel: int.MaxValue,
			pTableMaxReaderCount: 5);
		_tableIndex.Initialize(0);

		_sut = new LogV2StreamExistenceFilterInitializer(
			tfReaderFactory: () => new TFReaderLease(_log),
			tableIndex: _tableIndex);
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		_filter = new MockExistenceFilter(hasher);

	}

	private void AddEventToSut(string stream, int eventNumber) {
		var record = LogRecord.SingleWrite(
			factory: new LogV2RecordFactory(),
			logPosition: _nextLogPosition,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			eventStreamId: stream,
			expectedVersion: eventNumber - 1,
			eventType: "eventType",
			data: ReadOnlyMemory<byte>.Empty,
			metadata: ReadOnlyMemory<byte>.Empty);

		_log.AddRecord(record, _nextLogPosition);
		_tableIndex.Add(
			commitPos: _nextLogPosition,
			streamId: stream,
			version: record.ExpectedVersion + 1,
			position: _nextLogPosition);

		_nextLogPosition += _recordOffset;
	}

	[Fact]
	public async Task can_initialize_empty() {
		Assert.Equal(-1, _filter.CurrentCheckpoint);
		await _sut.Initialize(_filter, 0, CancellationToken.None);
		Assert.Equal(-1, _filter.CurrentCheckpoint);
		Assert.Empty(_filter.Hashes);
		Assert.Equal(1, _log.NumReads);
	}

	[Theory]
	[InlineData(100, 600)] // if we do truncate (to 100)
	[InlineData(1000, 1000)] // if we dont truncate
	[InlineData(2000, 1000)] // if we dont truncate
	public async Task can_truncate(long truncateTo, long expectedCheckpoint) {
		_filter.CurrentCheckpoint = 1000;

		AddEventToSut("1", 0);
		AddEventToSut("2", 0);
		AddEventToSut("3", 0);
		AddEventToSut("4", 0);
		AddEventToSut("5", 0);
		AddEventToSut("6", 0);

		await _sut.Initialize(_filter, truncateTo, CancellationToken.None);

		Assert.Equal(expectedCheckpoint, _filter.CurrentCheckpoint);
	}

	[Fact]
	public async Task can_initialize_from_beginning() {
		// (implementation detail: initializes from index)
		AddEventToSut("1", 0);
		AddEventToSut("1", 1);
		AddEventToSut("2", 0);
		AddEventToSut("2", 1);
		AddEventToSut("3", 0);
		AddEventToSut("3", 1);

		await _sut.Initialize(_filter, 0, CancellationToken.None);

		Assert.Equal(600, _filter.CurrentCheckpoint);
		Assert.Equal(3, _filter.Hashes.Count);
		Assert.Equal(2, _log.NumReads);
	}

	// this ensures every record is processed because each stream has 1 record
	[Fact]
	public async Task can_initialize_from_beginning_unique() {
		// (implementation detail: initializes from index)
		AddEventToSut("1", 0);
		AddEventToSut("2", 0);
		AddEventToSut("3", 0);
		AddEventToSut("4", 0);
		AddEventToSut("5", 0);
		AddEventToSut("6", 0);

		await _sut.Initialize(_filter, 0, CancellationToken.None);

		Assert.Equal(600, _filter.CurrentCheckpoint);
		Assert.Equal(6, _filter.Hashes.Count);
		Assert.Equal(2, _log.NumReads);
	}

	[Fact]
	public async Task can_initialize_incremental() {
		// (implementation detail: initializes from log)
		_filter.CurrentCheckpoint = 0;

		AddEventToSut("1", 0);
		AddEventToSut("1", 1);
		AddEventToSut("2", 0);
		AddEventToSut("2", 1);
		AddEventToSut("3", 0);
		AddEventToSut("3", 1);

		await _sut.Initialize(_filter, 0, CancellationToken.None);

		Assert.Equal(600, _filter.CurrentCheckpoint);
		Assert.Equal(3, _filter.Hashes.Count);
		Assert.Equal(7, _log.NumReads);
	}

	// while initialising from the index there might be an index merge causing a filedeletedexception
	// before the fix this test fairly reliably fails on my machine due to the indexes being deleted.
	// after the fix the window for getting the filedeletedexception is small so the test
	// is unlikely to exercise it.
	[Fact(Skip = "manual")]
	public async Task can_initalize_during_merge() {
		var eventsPerStream = 1_000;
		var numStreams = 1_000;
		var numEvents = eventsPerStream * numStreams;
		for (int i = 0; i < numStreams; i++) {
			for (int j = 0; j < eventsPerStream; j++) {
				AddEventToSut(stream: $"stream-{i}", eventNumber: j);
			}
		}

		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		// addDelayMs: we want to initialize the filter slowly, to give the ptables longer to move around
		var slowFilter = new MockExistenceFilter(hasher, addDelayMs: 1);
		await _sut.Initialize(slowFilter, 0, CancellationToken.None);

		Assert.Equal(numEvents * _recordOffset, slowFilter.CurrentCheckpoint);
		Assert.Equal(numStreams, slowFilter.Hashes.Count);
		Assert.Equal(2, _log.NumReads);
	}

	[Fact]
	public async Task cannot_initialize_with_v1_indexes() {
		var tableIndex = new TableIndex<string>(
			directory: Fixture.Directory,
			lowHasher: new XXHashUnsafe(),
			highHasher: new Murmur3AUnsafe(),
			emptyStreamId: string.Empty,
			memTableFactory: () => new HashListMemTable(
				version: PTableVersions.IndexV1,
				maxSize: 1_000_000 * 2),
			tfReaderFactory: () => throw new Exception("index tried to read the log"),
			ptableVersion: PTableVersions.IndexV1,
			maxAutoMergeIndexLevel: int.MaxValue,
			pTableMaxReaderCount: 5);
		tableIndex.Initialize(0);

		var sut = new LogV2StreamExistenceFilterInitializer(
			tfReaderFactory: () => throw new Exception("initializer tried to read the log"),
			tableIndex: tableIndex);

		var filter = new MockExistenceFilter(hasher: null);

		var ex = await Assert.ThrowsAsync<NotSupportedException>(async () => {
			await sut.Initialize(filter, 0, CancellationToken.None);
		});
		Assert.Equal(
			"The Stream Existence Filter is not supported with V1 index files. " +
			"Please disable the filter by setting StreamExistenceFilterSize to 0, or rebuild the indexes.",
			ex.Message);
		Assert.Equal(0, _log.NumReads);
	}
}
