// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Index;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using static KurrentDB.Common.Configuration.MetricsConfiguration;

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public sealed class ReadIndex<TStreamId> : IDisposable, IReadIndex<TStreamId> {
	public long LastIndexedPosition {
		get { return _indexCommitter.LastIndexedPosition; }
	}

	public IIndexWriter<TStreamId> IndexWriter {
		get { return _indexWriter; }
	}

	public IIndexReader<TStreamId> IndexReader {
		get { return _indexReader; }
	}

	public IIndexCommitter<TStreamId> IndexCommitter {
		get { return _indexCommitter; }
	}

	private readonly IIndexReader<TStreamId> _indexReader;
	private readonly IIndexWriter<TStreamId> _indexWriter;
	private readonly IIndexCommitter<TStreamId> _indexCommitter;
	private readonly IAllReader _allReader;
	private readonly IValueLookup<TStreamId> _streamIds;
	private readonly INameLookup<TStreamId> _streamNames;

	public ReadIndex(IPublisher bus,
		ObjectPool<ITransactionFileReader> readerPool,
		ITableIndex<TStreamId> tableIndex,
		INameIndexConfirmer<TStreamId> streamNameIndex,
		IValueLookup<TStreamId> streamIds,
		IStreamNamesProvider<TStreamId> streamNamesProvider,
		TStreamId emptyStreamName,
		IValidator<TStreamId> streamIdValidator,
		ISizer<TStreamId> sizer,
		INameExistenceFilter streamExistenceFilter,
		IExistenceFilterReader<TStreamId> streamExistenceFilterReader,
		INameIndexConfirmer<TStreamId> eventTypeIndex,
		ILRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached> streamLastEventNumberCache,
		ILRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached> streamMetadataCache,
		bool additionalCommitChecks,
		long metastreamMaxCount,
		int hashCollisionReadLimit,
		bool skipIndexScanOnReads,
		IReadOnlyCheckpoint replicationCheckpoint,
		ICheckpoint indexCheckpoint,
		IIndexStatusTracker indexStatusTracker,
		IIndexTracker indexTracker,
		ICacheHitsMissesTracker cacheTracker) {

		Ensure.NotNull(bus, "bus");
		Ensure.NotNull(readerPool, "readerPool");
		Ensure.NotNull(tableIndex, "tableIndex");
		Ensure.NotNull(streamIds, nameof(streamIds));
		Ensure.NotNull(streamNamesProvider, nameof(streamNamesProvider));
		Ensure.NotNull(streamIdValidator, nameof(streamIdValidator));
		Ensure.NotNull(sizer, nameof(sizer));
		Ensure.NotNull(streamExistenceFilter, nameof(streamExistenceFilter));
		Ensure.NotNull(streamExistenceFilterReader, nameof(streamExistenceFilterReader));
		Ensure.NotNull(streamLastEventNumberCache, nameof(streamLastEventNumberCache));
		Ensure.NotNull(streamMetadataCache, nameof(streamMetadataCache));
		Ensure.Positive(metastreamMaxCount, "metastreamMaxCount");
		Ensure.NotNull(replicationCheckpoint, "replicationCheckpoint");
		Ensure.NotNull(indexCheckpoint, "indexCheckpoint");

		var metastreamMetadata = new StreamMetadata(maxCount: metastreamMaxCount);

		var indexBackend = new IndexBackend<TStreamId>(readerPool, streamLastEventNumberCache, streamMetadataCache);

		_indexReader = new IndexReader<TStreamId>(indexBackend, tableIndex, streamNamesProvider, streamIdValidator,
			streamExistenceFilterReader, metastreamMetadata, hashCollisionReadLimit, skipIndexScanOnReads);

		_streamIds = streamIds;
		_streamNames = streamNamesProvider.StreamNames;
		var systemStreams = streamNamesProvider.SystemStreams;
		var eventTypeNames = streamNamesProvider.EventTypes;
		var streamExistenceFilterInitializer = streamNamesProvider.StreamExistenceFilterInitializer;

		_indexWriter = new IndexWriter<TStreamId>(indexBackend, _indexReader, _streamIds, _streamNames, systemStreams, emptyStreamName, sizer);
		_indexCommitter = new IndexCommitter<TStreamId>(bus, indexBackend, _indexReader, tableIndex, streamNameIndex,
			_streamNames, eventTypeIndex, eventTypeNames, systemStreams, streamExistenceFilter,
			streamExistenceFilterInitializer, indexCheckpoint, indexStatusTracker, indexTracker, additionalCommitChecks);
		_allReader = new AllReader<TStreamId>(indexBackend, _indexCommitter, _streamNames, eventTypeNames);

		RegisterHitsMisses(cacheTracker);
	}

	ValueTask<IndexReadEventResult> IReadIndex<TStreamId>.ReadEvent(string streamName, TStreamId streamId, long eventNumber, CancellationToken token) {
		return _indexReader.ReadEvent(streamName, streamId, eventNumber, token);
	}

	ValueTask<IndexReadStreamResult> IReadIndex<TStreamId>.ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token) {
		return _indexReader.ReadStreamEventsForward(streamName, streamId, fromEventNumber, maxCount, token);
	}

	ValueTask<IndexReadStreamResult> IReadIndex<TStreamId>.ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token) {
		return _indexReader.ReadStreamEventsBackward(streamName, streamId, fromEventNumber, maxCount, token);
	}

	TStreamId IReadIndex<TStreamId>.GetStreamId(string streamName) {
		return _streamIds.LookupValue(streamName);
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber, CancellationToken token) {
		return _indexReader.ReadEventInfo_KeepDuplicates(streamId, eventNumber, token);
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token) {
		return _indexReader.ReadEventInfoForward_KnownCollisions(streamId, fromEventNumber, maxCount, beforePosition, token);
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token) {
		return _indexReader.ReadEventInfoForward_NoCollisions(stream, fromEventNumber, maxCount, beforePosition, token);
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token) {
		return _indexReader.ReadEventInfoBackward_KnownCollisions(streamId, fromEventNumber, maxCount, beforePosition, token);
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId,
		long fromEventNumber, int maxCount, long beforePosition, CancellationToken token) {
		return _indexReader.ReadEventInfoBackward_NoCollisions(stream, getStreamId, fromEventNumber, maxCount, beforePosition, token);
	}

	ValueTask<string> IReadIndex<TStreamId>.GetStreamName(TStreamId streamId, CancellationToken token) {
		return _streamNames.LookupName(streamId, token);
	}

	async ValueTask<bool> IReadIndex<TStreamId>.IsStreamDeleted(TStreamId streamId, CancellationToken token) {
		return await _indexReader.GetStreamLastEventNumber(streamId, token) is EventNumber.DeletedStream;
	}

	ValueTask<long> IReadIndex<TStreamId>.GetStreamLastEventNumber(TStreamId streamId, CancellationToken token) {
		return _indexReader.GetStreamLastEventNumber(streamId, token);
	}

	public ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, CancellationToken token) {
		return _indexReader.GetStreamLastEventNumber_KnownCollisions(streamId, beforePosition, token);
	}

	public ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition, CancellationToken token) {
		return _indexReader.GetStreamLastEventNumber_NoCollisions(stream, getStreamId, beforePosition, token);
	}

	ValueTask<StreamMetadata> IReadIndex<TStreamId>.GetStreamMetadata(TStreamId streamId, CancellationToken token) {
		return _indexReader.GetStreamMetadata(streamId, token);
	}

	public ValueTask<TStreamId> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token) {
		return _indexReader.GetEventStreamIdByTransactionId(transactionId, token);
	}

	ValueTask<IndexReadAllResult> IReadIndex.ReadAllEventsForward(TFPos pos, int maxCount, CancellationToken token) {
		return _allReader.ReadAllEventsForward(pos, maxCount, token);
	}

	ValueTask<IndexReadAllResult> IReadIndex.ReadAllEventsForwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter, CancellationToken token) {
		return _allReader.FilteredReadAllEventsForward(pos, maxCount, maxSearchWindow, eventFilter, token);
	}

	ValueTask<IndexReadAllResult> IReadIndex.ReadAllEventsBackwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter, CancellationToken token) {
		return _allReader.FilteredReadAllEventsBackward(pos, maxCount, maxSearchWindow, eventFilter, token);
	}

	ValueTask<IndexReadAllResult> IReadIndex.ReadAllEventsBackward(TFPos pos, int maxCount, CancellationToken token) {
		return _allReader.ReadAllEventsBackward(pos, maxCount, token);
	}

	public ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(TStreamId streamId, CancellationToken token) {
		return _indexReader.GetEffectiveAcl(streamId, token);
	}

	void RegisterHitsMisses(ICacheHitsMissesTracker tracker) {
		tracker.Register(
			Cache.Chunk,
			() => Interlocked.Read(ref TFChunkReader.CachedReads),
			() => Interlocked.Read(ref TFChunkReader.NotCachedReads));

		tracker.Register(
			Cache.StreamInfo,
			() => _indexReader.CachedStreamInfo,
			() => _indexReader.NotCachedStreamInfo);
	}

	ReadIndexStats IReadIndex.GetStatistics() {
		return new ReadIndexStats(Interlocked.Read(ref TFChunkReader.CachedReads),
			Interlocked.Read(ref TFChunkReader.NotCachedReads),
			_indexReader.CachedStreamInfo,
			_indexReader.NotCachedStreamInfo,
			_indexReader.HashCollisions,
			_indexWriter.CachedTransInfo,
			_indexWriter.NotCachedTransInfo);
	}

	public void Close() {
		Dispose();
	}

	public void Dispose() {
		_indexCommitter.Dispose();
	}
}
