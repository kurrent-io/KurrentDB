using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.TransactionLog;

namespace EventStore.Core.Services.Storage.ReaderIndex {
	public interface IIndexBackend {
		TFReaderLease BorrowReader();
		void SetSystemSettings(SystemSettings systemSettings);
		SystemSettings GetSystemSettings();
	}

	public interface IIndexBackend<TStreamId> : IIndexBackend {
		IndexBackend<TStreamId>.EventNumberCached TryGetStreamLastEventNumber(TStreamId streamId);
		IndexBackend<TStreamId>.MetadataCached TryGetStreamMetadata(TStreamId streamId);

		long? UpdateStreamLastEventNumber(int cacheVersion, TStreamId streamId, long? lastEventNumber);
		StreamMetadata UpdateStreamMetadata(int cacheVersion, TStreamId streamId, StreamMetadata metadata);

		long? SetStreamLastEventNumber(TStreamId streamId, long lastEventNumber);
		StreamMetadata SetStreamMetadata(TStreamId streamId, StreamMetadata metadata);
	}

	public class IndexBackend<TStreamId> : IIndexBackend<TStreamId> {
		private readonly ObjectPool<ITransactionFileReader> _readers;
		private readonly ILRUCache<TStreamId, EventNumberCached> _streamLastEventNumberCache;
		private readonly ILRUCache<TStreamId, MetadataCached> _streamMetadataCache;
		private SystemSettings _systemSettings;

		public IndexBackend(
			ObjectPool<ITransactionFileReader> readers,
			ILRUCache<TStreamId, EventNumberCached> streamLastEventNumberCache,
			ILRUCache<TStreamId, MetadataCached> streamMetadataCache) {

			Ensure.NotNull(readers, nameof(readers));
			Ensure.NotNull(streamLastEventNumberCache, nameof(streamLastEventNumberCache));
			Ensure.NotNull(streamMetadataCache, nameof(streamMetadataCache));

			_readers = readers;
			_streamLastEventNumberCache = streamLastEventNumberCache;
			_streamMetadataCache = streamMetadataCache;
		}

		public TFReaderLease BorrowReader() {
			return new TFReaderLease(_readers);
		}

		public EventNumberCached TryGetStreamLastEventNumber(TStreamId streamId) {
			EventNumberCached cacheInfo;
			_streamLastEventNumberCache.TryGet(streamId, out cacheInfo);
			return cacheInfo;
		}

		public MetadataCached TryGetStreamMetadata(TStreamId streamId) {
			MetadataCached cacheInfo;
			_streamMetadataCache.TryGet(streamId, out cacheInfo);
			return cacheInfo;
		}

		public long? UpdateStreamLastEventNumber(int cacheVersion, TStreamId streamId, long? lastEventNumber) {
			var res = _streamLastEventNumberCache.Put(
				streamId,
				new KeyValuePair<int, long?>(cacheVersion, lastEventNumber),
				(key, d) => d.Key == 0 ? new EventNumberCached(1, d.Value) : new EventNumberCached(1, null),
				(key, old, d) => old.Version == d.Key
					? new EventNumberCached(d.Key + 1, d.Value ?? old.LastEventNumber)
					: old);
			return res.LastEventNumber;
		}

		public StreamMetadata UpdateStreamMetadata(int cacheVersion, TStreamId streamId, StreamMetadata metadata) {
			var res = _streamMetadataCache.Put(
				streamId,
				new KeyValuePair<int, StreamMetadata>(cacheVersion, metadata),
				(key, d) => d.Key == 0 ? new MetadataCached(1, d.Value) : new MetadataCached(1, null),
				(key, old, d) => old.Version == d.Key ? new MetadataCached(d.Key + 1, d.Value ?? old.Metadata) : old);
			return res.Metadata;
		}

		long? IIndexBackend<TStreamId>.SetStreamLastEventNumber(TStreamId streamId, long lastEventNumber) {
			var res = _streamLastEventNumberCache.Put(streamId,
				lastEventNumber,
				(key, lastEvNum) => new EventNumberCached(1, lastEvNum),
				(key, old, lastEvNum) => new EventNumberCached(old.Version + 1, lastEvNum));
			return res.LastEventNumber;
		}

		StreamMetadata IIndexBackend<TStreamId>.SetStreamMetadata(TStreamId streamId, StreamMetadata metadata) {
			var res = _streamMetadataCache.Put(streamId,
				metadata,
				(key, meta) => new MetadataCached(1, meta),
				(key, old, meta) => new MetadataCached(old.Version + 1, meta));
			return res.Metadata;
		}

		public void SetSystemSettings(SystemSettings systemSettings) {
			_systemSettings = systemSettings;
		}

		public SystemSettings GetSystemSettings() {
			return _systemSettings;
		}

		public struct EventNumberCached {
			public readonly int Version;
			public readonly long? LastEventNumber;

			public EventNumberCached(int version, long? lastEventNumber) {
				Version = version;
				LastEventNumber = lastEventNumber;
			}

			public static int ApproximateSize => Unsafe.SizeOf<EventNumberCached>();
		}

		public struct MetadataCached {
			public readonly int Version;
			public readonly StreamMetadata Metadata;

			public MetadataCached(int version, StreamMetadata metadata) {
				Version = version;
				Metadata = metadata;
			}

			public int ApproximateSize => Unsafe.SizeOf<MetadataCached>() + (Metadata?.ApproximateSize ?? 0);
		}
	}
}
