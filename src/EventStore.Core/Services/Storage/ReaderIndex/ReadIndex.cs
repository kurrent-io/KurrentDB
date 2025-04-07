// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using static EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public sealed class ReadIndex<TStreamId> : IDisposable, IReadIndex<TStreamId> {
	public long LastIndexedPosition {
		get { return _indexCommitter.LastIndexedPosition; }
	}

	public IIndexWriter<TStreamId> IndexWriter {
		get { return _indexWriter; }
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

	IndexReadEventResult IReadIndex<TStreamId>.ReadEvent(string streamName, TStreamId streamId, long eventNumber) {
		return _indexReader.ReadEvent(streamName, streamId, eventNumber);
	}

	IndexReadStreamResult IReadIndex<TStreamId>.ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount) {
		return _indexReader.ReadStreamEventsForward(streamName, streamId, fromEventNumber, maxCount);
	}

	IndexReadStreamResult IReadIndex<TStreamId>.ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount) {
		return _indexReader.ReadStreamEventsBackward(streamName, streamId, fromEventNumber, maxCount);
	}

	TStreamId IReadIndex<TStreamId>.GetStreamId(string streamName) {
		return _streamIds.LookupValue(streamName);
	}

	public IndexReadEventInfoResult ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber) {
		return _indexReader.ReadEventInfo_KeepDuplicates(streamId, eventNumber);
	}

	public IndexReadEventInfoResult ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition) {
		return _indexReader.ReadEventInfoForward_KnownCollisions(streamId, fromEventNumber, maxCount, beforePosition);
	}

	public IndexReadEventInfoResult ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition) {
		return _indexReader.ReadEventInfoForward_NoCollisions(stream, fromEventNumber, maxCount, beforePosition);
	}

	public IndexReadEventInfoResult ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
		long beforePosition) {
		return _indexReader.ReadEventInfoBackward_KnownCollisions(streamId, fromEventNumber, maxCount, beforePosition);
	}

	public IndexReadEventInfoResult ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId,
		long fromEventNumber, int maxCount, long beforePosition) {
		return _indexReader.ReadEventInfoBackward_NoCollisions(stream, getStreamId, fromEventNumber, maxCount, beforePosition);
	}

	string IReadIndex<TStreamId>.GetStreamName(TStreamId streamId) {
		return _streamNames.LookupName(streamId);
	}

	bool IReadIndex<TStreamId>.IsStreamDeleted(TStreamId streamId) {
		return _indexReader.GetStreamLastEventNumber(streamId) == EventNumber.DeletedStream;
	}

	long IReadIndex<TStreamId>.GetStreamLastEventNumber(TStreamId streamId) {
		return _indexReader.GetStreamLastEventNumber(streamId);
	}

	public long GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition) {
		return _indexReader.GetStreamLastEventNumber_KnownCollisions(streamId, beforePosition);
	}

	public long GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition) {
		return _indexReader.GetStreamLastEventNumber_NoCollisions(stream, getStreamId, beforePosition);
	}

	StreamMetadata IReadIndex<TStreamId>.GetStreamMetadata(TStreamId streamId) {
		return _indexReader.GetStreamMetadata(streamId);
	}

	public TStreamId GetEventStreamIdByTransactionId(long transactionId) {
		return _indexReader.GetEventStreamIdByTransactionId(transactionId);
	}

	IndexReadAllResult IReadIndex.ReadAllEventsForward(TFPos pos, int maxCount) {
		return _allReader.ReadAllEventsForward(pos, maxCount);
	}

	IndexReadAllResult IReadIndex.ReadAllEventsForwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter) {
		return _allReader.FilteredReadAllEventsForward(pos, maxCount, maxSearchWindow, eventFilter);
	}

	IndexReadAllResult IReadIndex.ReadAllEventsBackwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter) {
		return _allReader.FilteredReadAllEventsBackward(pos, maxCount, maxSearchWindow, eventFilter);
	}

	IndexReadAllResult IReadIndex.ReadAllEventsBackward(TFPos pos, int maxCount) {
		return _allReader.ReadAllEventsBackward(pos, maxCount);
	}

	public StorageMessage.EffectiveAcl GetEffectiveAcl(TStreamId streamId) {
		return _indexReader.GetEffectiveAcl(streamId);
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
