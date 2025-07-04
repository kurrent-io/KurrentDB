// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.SecondaryIndexing.Tests.Fakes;

public class DummyReadIndex: IReadIndex<string> {
	public long LastIndexedPosition { get; }
	public ReadIndexStats GetStatistics() {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadAllResult> ReadAllEventsForward(TFPos pos, int maxCount, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadAllResult> ReadAllEventsBackward(TFPos pos, int maxCount, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadAllResult> ReadAllEventsForwardFiltered(TFPos pos, int maxCount, int maxSearchWindow, IEventFilter eventFilter,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadAllResult> ReadAllEventsBackwardFiltered(TFPos pos, int maxCount, int maxSearchWindow, IEventFilter eventFilter,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public void Close() {

	}

	public void Dispose() {

	}

	public IIndexWriter<string> IndexWriter { get; } = null!;
	public IIndexReader<string> IndexReader { get; }= new FakeIndexReader();
	public ValueTask<IndexReadEventResult> ReadEvent(string streamName, string streamId, long eventNumber, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(string streamName, string streamId, long fromEventNumber, int maxCount,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadStreamResult> ReadStreamEventsForward(string streamName, string streamId, long fromEventNumber, int maxCount,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(string streamId, long eventNumber, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(string streamId, long fromEventNumber, int maxCount, long beforePosition,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(string streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, string> getStreamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<bool> IsStreamDeleted(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<long> GetStreamLastEventNumber(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<long> GetStreamLastEventNumber_KnownCollisions(string streamId, long beforePosition, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, string> getStreamId, long beforePosition,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<StreamMetadata> GetStreamMetadata(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<string> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public string GetStreamId(string streamName) {
		throw new NotImplementedException();
	}

	public ValueTask<string> GetStreamName(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}
}

public class FakeIndexReader: IIndexReader<string> {
	public long CachedStreamInfo { get; }
	public long NotCachedStreamInfo { get; }
	public long HashCollisions { get; }
	public IIndexBackend<string> Backend { get; } = new FakeIndexBackend();
	public ValueTask<IndexReadEventResult> ReadEvent(string streamName, string streamId, long eventNumber, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadStreamResult> ReadStreamEventsForward(string streamName, string streamId, long fromEventNumber, int maxCount,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(string streamName, string streamId, long fromEventNumber, int maxCount,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(string streamId, long eventNumber, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(string streamId, long fromEventNumber, int maxCount, long beforePosition,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(string streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, string> getStreamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<IPrepareLogRecord<string>> ReadPrepare(string streamId, long eventNumber, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<string> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<StreamMetadata> GetStreamMetadata(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<long> GetStreamLastEventNumber(string streamId, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<long> GetStreamLastEventNumber_KnownCollisions(string streamId, long beforePosition, CancellationToken token) {
		throw new NotImplementedException();
	}

	public ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, string> getStreamId, long beforePosition,
		CancellationToken token) {
		throw new NotImplementedException();
	}

	public TFReaderLease BorrowReader() {
		throw new NotImplementedException();
	}
}

public class FakeIndexBackend: IIndexBackend<string>{
	public TFReaderLease BorrowReader() {
		throw new NotImplementedException();
	}

	public void SetSystemSettings(SystemSettings systemSettings) {
		throw new NotImplementedException();
	}

	public SystemSettings GetSystemSettings() {
		throw new NotImplementedException();
	}

	public IndexBackend<string>.EventNumberCached TryGetStreamLastEventNumber(string streamId) {
		return new IndexBackend<string>.EventNumberCached(1, null, null);
	}

	public IndexBackend<string>.MetadataCached TryGetStreamMetadata(string streamId) {
		throw new NotImplementedException();
	}

	public long? UpdateStreamLastEventNumber(int cacheVersion, string streamId, long? lastEventNumber) {
		throw new NotImplementedException();
	}

	public long? UpdateStreamSecondaryIndexId(int cacheVersion, string streamId, long? secondaryIndexId) {
		return secondaryIndexId;
	}

	public StreamMetadata UpdateStreamMetadata(int cacheVersion, string streamId, StreamMetadata metadata) {
		throw new NotImplementedException();
	}

	public long? SetStreamLastEventNumber(string streamId, long lastEventNumber) {
		throw new NotImplementedException();
	}

	public long? SetStreamSecondaryIndexId(string streamId, long secondaryIndexId) {
		throw new NotImplementedException();
	}

	public StreamMetadata SetStreamMetadata(string streamId, StreamMetadata metadata) {
		throw new NotImplementedException();
	}
}
