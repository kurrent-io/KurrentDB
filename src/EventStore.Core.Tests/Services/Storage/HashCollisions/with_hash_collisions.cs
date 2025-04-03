// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using NUnit.Framework;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Services.Storage.ReaderIndex;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;
using EventStore.Core.Tests.Index.Hashers;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions;

// both the stream names hash to the same value using XXHash.
// they are different for Murmur3 so we use only XXHashUnsafe here.
// we use a fake TransactionFileReader that attributes:
//    odd  positions to LPN-FC002_LPK51001
//    even positions to account--696193173
[TestFixture]
public class HashCollisionTestFixture : SpecificationWithDirectoryPerTestFixture {
	protected int _hashCollisionReadLimit = 5;
	protected int _maxMemTableSize = 5;
	protected TableIndex<string> _tableIndex;
	protected IIndexReader<string> _indexReader;
	protected IIndexBackend<string> _indexBackend;
	protected IHasher<string> _lowHasher;
	protected IHasher<string> _highHasher;
	protected string _indexDir;
	protected TFReaderLease _fakeReader;
	protected LogFormatAbstractor<string> _logFormat;

	protected virtual void given() {
	}

	protected virtual void when() {
	}

	[OneTimeSetUp]
	public void Setup() {
		given();
		_indexDir = PathName;
		_fakeReader = new TFReaderLease(new FakeReader());
		_indexBackend = new FakeIndexBackend<string>(_fakeReader);

		_logFormat = LogFormatHelper<LogFormat.V2, string>.LogFormatFactory.Create(new() {
			InMemory = true,
			LowHasher = new XXHashUnsafe(),
			HighHasher = new ConstantHasher(0),
		});

		_lowHasher = _logFormat.LowHasher;
		_highHasher = _logFormat.HighHasher;
		_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, _logFormat.EmptyStreamId,
			() => new HashListMemTable(PTableVersions.IndexV4, maxSize: _maxMemTableSize),
			() => _fakeReader,
			PTableVersions.IndexV4,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: _maxMemTableSize,
			maxTablesPerLevel: 2);
		_logFormat.StreamNamesProvider.SetTableIndex(_tableIndex);
		_tableIndex.Initialize(long.MaxValue);
		_indexReader = new IndexReader<string>(_indexBackend, _tableIndex,
			_logFormat.StreamNamesProvider,
			_logFormat.StreamIdValidator,
			_logFormat.StreamExistenceFilterReader,
			new EventStore.Core.Data.StreamMetadata(),
			_hashCollisionReadLimit, skipIndexScanOnRead: false);

		when();
		//wait for the mem table to be dumped
		System.Threading.Thread.Sleep(500);
	}

	public override Task TestFixtureTearDown() {
		_logFormat.Dispose();
		_tableIndex.Close();
		return base.TestFixtureTearDown();
	}
}

class UseMaxAgeFixtureArgs: IEnumerable
{
	public IEnumerator GetEnumerator()
	{
		yield return true;
		yield return false;
	}
}

[TestFixture]
public class when_stream_does_not_exist : HashCollisionTestFixture {
	protected override void given() {
		_hashCollisionReadLimit = 5;
	}

	protected override void when() {
		//mem table
		_tableIndex.Add(1, "LPN-FC002_LPK51001", 0, 3);
		_tableIndex.Add(1, "LPN-FC002_LPK51001", 1, 5);
	}

	[Test]
	public async Task should_return_no_stream() {
		Assert.AreEqual(ExpectedVersion.NoStream, await _indexReader.GetStreamLastEventNumber("account--696193173", CancellationToken.None));
	}
}

[TestFixture]
[TestFixtureSource(typeof(UseMaxAgeFixtureArgs))]
public class when_stream_is_out_of_range_of_read_limit : HashCollisionTestFixture {
	private readonly bool _useMaxAge;
	private readonly string stream1Id = "account--696193173";
	private readonly string stream2Id = "LPN-FC002_LPK51001";

	public when_stream_is_out_of_range_of_read_limit(bool useMaxAge) {
		_useMaxAge = useMaxAge;
	}

	protected override void given() {
		_hashCollisionReadLimit = 1;
	}

	protected override void when() {
		if (_useMaxAge) {
			_indexBackend.SetStreamMetadata(stream1Id, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
			_indexBackend.SetStreamMetadata(stream2Id, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
		}
		//ptable 1
		_tableIndex.Add(1, stream1Id, 0, 0);
		_tableIndex.Add(1, stream2Id, 0, 3);
		_tableIndex.Add(1, stream2Id, 1, 5);
		_tableIndex.Add(1, stream2Id, 2, 7);
		_tableIndex.Add(1, stream2Id, 3, 9);
		//mem table
		_tableIndex.Add(1, stream2Id, 4, 13);
	}

	[Test]
	public async Task should_return_invalid_event_number() {
		Assert.AreEqual(EventStore.Core.Data.EventNumber.Invalid,
			await _indexReader.GetStreamLastEventNumber(stream1Id, CancellationToken.None));
	}
}

[TestFixture]
[TestFixtureSource(typeof(UseMaxAgeFixtureArgs))]
public class when_stream_is_in_of_range_of_read_limit : HashCollisionTestFixture {
	private readonly bool _useMaxAge;
	private readonly string stream1Id = "account--696193173";
	private readonly string stream2Id = "LPN-FC002_LPK51001";

	public when_stream_is_in_of_range_of_read_limit(bool useMaxAge) {
		_useMaxAge = useMaxAge;
	}

	protected override void given() {
		_hashCollisionReadLimit = 5;
	}

	protected override void when() {
		if (_useMaxAge) {
			_indexBackend.SetStreamMetadata(stream1Id, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
			_indexBackend.SetStreamMetadata(stream2Id, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
		}
		//ptable 1
		_tableIndex.Add(1, stream1Id, 0, 0);
		_tableIndex.Add(1, stream2Id, 0, 3);
		_tableIndex.Add(1, stream2Id, 1, 5);
		_tableIndex.Add(1, stream2Id, 2, 7);
		_tableIndex.Add(1, stream2Id, 3, 9);
		//mem table
		_tableIndex.Add(1, stream2Id, 4, 13);
	}

	[Test]
	public async Task should_return_last_event_number() {
		Assert.AreEqual(0, await _indexReader.GetStreamLastEventNumber(stream1Id, CancellationToken.None));
	}
}

[TestFixture]
[TestFixtureSource(typeof(UseMaxAgeFixtureArgs))]
public class when_hash_read_limit_is_not_reached : HashCollisionTestFixture {
	private readonly bool _useMaxAge;

	public when_hash_read_limit_is_not_reached(bool useMaxAge) {
		_useMaxAge = useMaxAge;
	}

	protected override void given() {
		_hashCollisionReadLimit = 3;
	}

	protected override void when() {
		string stream1Id = "account--696193173";
		string stream2Id = "LPN-FC002_LPK51001";
		if (_useMaxAge) {
			_indexBackend.SetStreamMetadata(stream1Id, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
			_indexBackend.SetStreamMetadata(stream2Id, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
		}
		//ptable 1
		_tableIndex.Add(1, stream1Id, 0, 0);
		_tableIndex.Add(1, stream2Id, 0, 3);
		_tableIndex.Add(1, stream2Id, 1, 5);
		_tableIndex.Add(1, stream2Id, 2, 7);
		_tableIndex.Add(1, stream2Id, 3, 9);
	}

	[Test]
	public async Task should_return_invalid_event_number() {
		Assert.AreEqual(EventStore.Core.Data.EventNumber.Invalid,
			await _indexReader.GetStreamLastEventNumber("account--696193173", CancellationToken.None));
	}
}

[TestFixture]
[TestFixtureSource(typeof(UseMaxAgeFixtureArgs))]
public class when_index_contains_duplicate_entries : HashCollisionTestFixture {
	private readonly string streamId = "account--696193173";
	private readonly bool _useMaxAge;

	public when_index_contains_duplicate_entries(bool useMaxAge) {
		_useMaxAge = useMaxAge;
	}
	protected override void given() {
		_hashCollisionReadLimit = 5;
	}

	protected override void when() {
		if (_useMaxAge) {
			_indexBackend.SetStreamMetadata(streamId, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
		}
		//ptable 1
		_tableIndex.Add(1, streamId, 0, 2);
		_tableIndex.Add(1, streamId, 0, 4);
		_tableIndex.Add(1, streamId, 1, 6);
		_tableIndex.Add(1, streamId, 2, 8);
	}

	[Test]
	public async Task should_be_able_to_read_stream_events_forward_and_exclude_duplicates() {
		var result = await _indexReader.ReadStreamEventsForward(streamId, 0, int.MaxValue, CancellationToken.None);
		Assert.AreEqual(3, result.Records.Length);

		Assert.AreEqual(streamId, result.Records[0].EventStreamId);
		Assert.AreEqual(0, result.Records[0].EventNumber);
		Assert.AreEqual(2, result.Records[0].LogPosition);

		Assert.AreEqual(streamId, result.Records[1].EventStreamId);
		Assert.AreEqual(1, result.Records[1].EventNumber);
		Assert.AreEqual(6, result.Records[1].LogPosition);

		Assert.AreEqual(streamId, result.Records[2].EventStreamId);
		Assert.AreEqual(2, result.Records[2].EventNumber);
		Assert.AreEqual(8, result.Records[2].LogPosition);
	}

	[Test]
	public async Task should_be_able_to_read_stream_events_backward_and_exclude_duplicates() {
		var result = await _indexReader.ReadStreamEventsBackward(streamId, 2, int.MaxValue, CancellationToken.None);
		Assert.AreEqual(3, result.Records.Length);

		Assert.AreEqual(streamId, result.Records[2].EventStreamId);
		Assert.AreEqual(0, result.Records[2].EventNumber);
		Assert.AreEqual(2, result.Records[2].LogPosition);

		Assert.AreEqual(streamId, result.Records[1].EventStreamId);
		Assert.AreEqual(1, result.Records[1].EventNumber);
		Assert.AreEqual(6, result.Records[1].LogPosition);

		Assert.AreEqual(streamId, result.Records[0].EventStreamId);
		Assert.AreEqual(2, result.Records[0].EventNumber);
		Assert.AreEqual(8, result.Records[0].LogPosition);
	}

	[Test]
	public async Task should_be_able_to_read_single_event_and_exclude_duplicates() {
		var result = await _indexReader.ReadEvent(streamId, 0, CancellationToken.None);

		Assert.AreEqual(streamId, result.Record.EventStreamId);
		Assert.AreEqual(0, result.Record.EventNumber);
		Assert.AreEqual(2, result.Record.LogPosition);
	}
}

[TestFixture]
[TestFixtureSource(typeof(UseMaxAgeFixtureArgs))]
public class
	when_index_contains_duplicate_entries_and_the_duplicate_is_a_64bit_index_entry : HashCollisionTestFixture {
	private readonly string streamId = "account--696193173";
	private readonly bool _useMaxAge;

	public when_index_contains_duplicate_entries_and_the_duplicate_is_a_64bit_index_entry(bool useMaxAge) {
		_useMaxAge = useMaxAge;
	}

	protected override void given() {
		_maxMemTableSize = 3;
		_hashCollisionReadLimit = 5;
	}

	protected override void when() {
		if (_useMaxAge) {
			_indexBackend.SetStreamMetadata(streamId, new StreamMetadata(maxAge:TimeSpan.FromDays(1)));
		}
		//ptable 1 with 32bit indexes
		_tableIndex.Add(1, streamId, 0, 2);
		_tableIndex.Add(1, streamId, 1, 4);
		_tableIndex.Add(1, streamId, 2, 6);
		System.Threading.Thread.Sleep(500);
		_tableIndex.Close(false);
		_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
			() => new HashListMemTable(PTableVersions.IndexV2, maxSize: _maxMemTableSize),
			() => _fakeReader,
			PTableVersions.IndexV2,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: _maxMemTableSize,
			maxTablesPerLevel: 2);
		_tableIndex.Initialize(long.MaxValue);
		_indexReader = new IndexReader<string>(
			_indexBackend, _tableIndex,
			_logFormat.StreamNamesProvider,
			_logFormat.StreamIdValidator,
			_logFormat.StreamExistenceFilterReader,
			new EventStore.Core.Data.StreamMetadata(),
			_hashCollisionReadLimit, skipIndexScanOnRead: false);
		//memtable with 64bit indexes
		_tableIndex.Add(1, streamId, 0, 8);
	}

	[Test]
	public async Task should_return_the_correct_last_event_number() {
		var result = await _indexReader.GetStreamLastEventNumber(streamId, CancellationToken.None);
		Assert.AreEqual(2, result);
	}

	[Test]
	public async Task should_be_able_to_read_stream_events_forward_and_exclude_duplicates() {
		var result = await _indexReader.ReadStreamEventsForward(streamId, 0, int.MaxValue, CancellationToken.None);
		Assert.AreEqual(3, result.Records.Length);

		Assert.AreEqual(streamId, result.Records[0].EventStreamId);
		Assert.AreEqual(0, result.Records[0].EventNumber);
		Assert.AreEqual(2, result.Records[0].LogPosition);

		Assert.AreEqual(streamId, result.Records[1].EventStreamId);
		Assert.AreEqual(1, result.Records[1].EventNumber);
		Assert.AreEqual(4, result.Records[1].LogPosition);

		Assert.AreEqual(streamId, result.Records[2].EventStreamId);
		Assert.AreEqual(2, result.Records[2].EventNumber);
		Assert.AreEqual(6, result.Records[2].LogPosition);
	}

	[Test]
	public async Task should_be_able_to_read_stream_events_backward_and_exclude_duplicates() {
		var result = await _indexReader.ReadStreamEventsBackward(streamId, 2, int.MaxValue, CancellationToken.None);
		Assert.AreEqual(3, result.Records.Length);

		Assert.AreEqual(streamId, result.Records[2].EventStreamId);
		Assert.AreEqual(0, result.Records[2].EventNumber);
		Assert.AreEqual(2, result.Records[2].LogPosition);

		Assert.AreEqual(streamId, result.Records[1].EventStreamId);
		Assert.AreEqual(1, result.Records[1].EventNumber);
		Assert.AreEqual(4, result.Records[1].LogPosition);

		Assert.AreEqual(streamId, result.Records[0].EventStreamId);
		Assert.AreEqual(2, result.Records[0].EventNumber);
		Assert.AreEqual(6, result.Records[0].LogPosition);
	}

	[Test]
	public async Task should_be_able_to_read_single_event_and_exclude_duplicates() {
		var result = await _indexReader.ReadEvent(streamId, 0, CancellationToken.None);

		Assert.AreEqual(streamId, result.Record.EventStreamId);
		Assert.AreEqual(0, result.Record.EventNumber);
		Assert.AreEqual(2, result.Record.LogPosition);
	}
}

[TestFixture]
public class when_stream_has_max_age : HashCollisionTestFixture {
	private readonly string _oddStream = "LPN-FC002_LPK51001";
	private readonly string _evenStream = "account--696193173";

	protected override void when() {
		_indexBackend.SetStreamMetadata(
			_evenStream,
			new StreamMetadata(maxAge: TimeSpan.FromDays(1)));

		_tableIndex.Add(1, _evenStream, 5, 0);
		_tableIndex.Add(1, _evenStream, 6, 2);
		_tableIndex.Add(1, _oddStream, 5, 3);
		_tableIndex.Add(1, _oddStream, 6, 5);
		_tableIndex.Add(1, _oddStream, 7, 7);
	}

	[Test]
	public async Task can_read() {
		var result = await _indexReader.ReadStreamEventsForward(
			streamName: _evenStream,
			fromEventNumber: 0,
			maxCount: 2,
			CancellationToken.None);

		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(5, result.Records[0].EventNumber);
		Assert.AreEqual(6, result.Records[1].EventNumber);
	}
}

public class FakeIndexBackend<TStreamId> : IIndexBackend<TStreamId> {
	private readonly TFReaderLease _readerLease;
	private readonly Dictionary<TStreamId, IndexBackend<TStreamId>.MetadataCached> _streamMetadata =
		new();

	public FakeIndexBackend(TFReaderLease readerLease) {
		_readerLease = readerLease;
	}

	public TFReaderLease BorrowReader() {
		return _readerLease;
	}

	public IndexBackend<TStreamId>.EventNumberCached TryGetStreamLastEventNumber(TStreamId streamId) {
		return new IndexBackend<TStreamId>.EventNumberCached(-1, null); //always return uncached
	}

	public IndexBackend<TStreamId>.MetadataCached TryGetStreamMetadata(TStreamId streamId) {
		if (_streamMetadata.TryGetValue(streamId, out var metadata))
			return metadata;
		return new IndexBackend<TStreamId>.MetadataCached();
	}

	public long? UpdateStreamLastEventNumber(int cacheVersion, TStreamId streamId, long? lastEventNumber) {
		return null;
	}

	public EventStore.Core.Data.StreamMetadata UpdateStreamMetadata(int cacheVersion, TStreamId streamId,
		EventStore.Core.Data.StreamMetadata metadata) {
		_streamMetadata[streamId] = new IndexBackend<TStreamId>.MetadataCached(1, metadata);
		return metadata;
	}

	public long? SetStreamLastEventNumber(TStreamId streamId, long lastEventNumber) {
		return null;
	}

	public EventStore.Core.Data.StreamMetadata SetStreamMetadata(TStreamId streamId,
		EventStore.Core.Data.StreamMetadata metadata) {
		_streamMetadata[streamId] = new IndexBackend<TStreamId>.MetadataCached(1, metadata);
		return metadata;
	}

	public void SetSystemSettings(EventStore.Core.Data.SystemSettings systemSettings) {
	}

	public EventStore.Core.Data.SystemSettings GetSystemSettings() {
		return null;
	}
}

public class FakeReader : ITransactionFileReader {
	public void Reposition(long position) {
		throw new NotImplementedException();
	}

	public ValueTask<SeqReadResult> TryReadNext(CancellationToken token)
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<SeqReadResult> TryReadPrev(CancellationToken token)
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token) {
		var record = (LogRecord)new PrepareLogRecord(position, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			position % 2 == 0 ? "account--696193173" : "LPN-FC002_LPK51001", null, -1, DateTime.UtcNow, PrepareFlags.None,
			"type", null, new byte[0], null);
		return new(new RecordReadResult(true, position + 1, record, 1));
	}

	public ValueTask<bool> ExistsAt(long position, CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled<bool>(token) : ValueTask.FromResult(true);
}
