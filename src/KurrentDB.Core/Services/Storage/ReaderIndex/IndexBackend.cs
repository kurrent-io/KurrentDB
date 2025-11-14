// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.TransactionLog;

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public interface IIndexBackend {
	ITransactionFileReader TFReader { get; }
	void SetSystemSettings(SystemSettings systemSettings);
	SystemSettings GetSystemSettings();
}

public interface IIndexBackend<TStreamId> : IIndexBackend {
	IndexBackend<TStreamId>.EventNumberCached TryGetStreamLastEventNumber(TStreamId streamId);
	IndexBackend<TStreamId>.MetadataCached TryGetStreamMetadata(TStreamId streamId);

	long? UpdateStreamLastEventNumber(int cacheVersion, TStreamId streamId, long? lastEventNumber);
	long? UpdateStreamSecondaryIndexId(int cacheVersion, TStreamId streamId, long? secondaryIndexId);
	StreamMetadata UpdateStreamMetadata(int cacheVersion, TStreamId streamId, StreamMetadata metadata);

	long? SetStreamLastEventNumber(TStreamId streamId, long lastEventNumber);
	long? SetStreamSecondaryIndexId(TStreamId streamId, long secondaryIndexId);
	StreamMetadata SetStreamMetadata(TStreamId streamId, StreamMetadata metadata);
}

public class IndexBackend<TStreamId> : IIndexBackend<TStreamId> {
	private readonly ILRUCache<TStreamId, EventNumberCached> _streamLastEventNumberCache;
	private readonly ILRUCache<TStreamId, MetadataCached> _streamMetadataCache;
	private SystemSettings _systemSettings;

	public IndexBackend(
		ITransactionFileReader tfReader,
		ILRUCache<TStreamId, EventNumberCached> streamLastEventNumberCache,
		ILRUCache<TStreamId, MetadataCached> streamMetadataCache) {
		TFReader = Ensure.NotNull(tfReader);
		_streamLastEventNumberCache = Ensure.NotNull(streamLastEventNumberCache);
		_streamMetadataCache = Ensure.NotNull(streamMetadataCache);
	}

	public ITransactionFileReader TFReader { get; }

	public EventNumberCached TryGetStreamLastEventNumber(TStreamId streamId) {
		_streamLastEventNumberCache.TryGet(streamId, out var cacheInfo);
		return cacheInfo;
	}

	public MetadataCached TryGetStreamMetadata(TStreamId streamId) {
		_streamMetadataCache.TryGet(streamId, out var cacheInfo);
		return cacheInfo;
	}

	public long? UpdateStreamLastEventNumber(int cacheVersion, TStreamId streamId, long? lastEventNumber) {
		var res = _streamLastEventNumberCache.Put(
			streamId,
			new KeyValuePair<int, long?>(cacheVersion, lastEventNumber),
			(_, d) => d.Key == 0 ? new EventNumberCached(1, d.Value, null) : new(1, null, null),
			(_, old, d) => old.Version == d.Key ? new(d.Key + 1, d.Value ?? old.LastEventNumber, old.SecondaryIndexId) : old);
		return res.LastEventNumber;
	}

	public long? UpdateStreamSecondaryIndexId(int cacheVersion, TStreamId streamId, long? secondaryIndexId) {
		var res = _streamLastEventNumberCache.Put(
			streamId,
			new KeyValuePair<int, long?>(cacheVersion, secondaryIndexId),
			(_, d) => d.Key == 0 ? new EventNumberCached(1, null, d.Value) : new(1, null, null),
			(_, old, d) => old.Version == d.Key ? new(d.Key + 1, old.LastEventNumber, d.Value ?? old.SecondaryIndexId) : old);
		return res.SecondaryIndexId;
	}

	public StreamMetadata UpdateStreamMetadata(int cacheVersion, TStreamId streamId, StreamMetadata metadata) {
		var res = _streamMetadataCache.Put(
			streamId,
			new KeyValuePair<int, StreamMetadata>(cacheVersion, metadata),
			(_, d) => d.Key == 0 ? new MetadataCached(1, d.Value) : new(1, null),
			(_, old, d) => old.Version == d.Key ? new(d.Key + 1, d.Value ?? old.Metadata) : old);
		return res.Metadata;
	}

	long? IIndexBackend<TStreamId>.SetStreamLastEventNumber(TStreamId streamId, long lastEventNumber) {
		var res = _streamLastEventNumberCache.Put(streamId,
			lastEventNumber,
			(_, lastEvNum) => new(1, lastEvNum, null),
			(_, old, lastEvNum) => new(old.Version + 1, lastEvNum, old.SecondaryIndexId));
		return res.LastEventNumber;
	}

	long? IIndexBackend<TStreamId>.SetStreamSecondaryIndexId(TStreamId streamId, long secondaryIndexId) {
		var res = _streamLastEventNumberCache.Put(streamId,
			secondaryIndexId,
			(_, secIndexId) => new(1, null, secIndexId),
			(_, old, secIndexId) => new(old.Version + 1, old.LastEventNumber, secIndexId));
		return res.SecondaryIndexId;
	}

	StreamMetadata IIndexBackend<TStreamId>.SetStreamMetadata(TStreamId streamId, StreamMetadata metadata) {
		var res = _streamMetadataCache.Put(streamId,
			metadata,
			(_, meta) => new(1, meta),
			(_, old, meta) => new(old.Version + 1, meta));
		return res.Metadata;
	}

	public void SetSystemSettings(SystemSettings systemSettings) {
		_systemSettings = systemSettings;
	}

	public SystemSettings GetSystemSettings() {
		return _systemSettings;
	}

	public struct EventNumberCached(int version, long? lastEventNumber, long? secondaryIndexId) {
		public readonly int Version = version;
		public readonly long? LastEventNumber = lastEventNumber;
		public readonly long? SecondaryIndexId = secondaryIndexId;

		public static int ApproximateSize => Unsafe.SizeOf<EventNumberCached>();
	}

	public readonly struct MetadataCached(int version, StreamMetadata metadata) {
		public readonly int Version = version;
		public readonly StreamMetadata Metadata = metadata;

		public int ApproximateSize => Unsafe.SizeOf<MetadataCached>() + (Metadata?.ApproximateSize ?? 0);
	}
}
