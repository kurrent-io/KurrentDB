// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNext.IO;
using DotNext.Runtime.InteropServices;
using KurrentDB.Common.Utils;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.DataStructures.ProbabilisticFilter;
using KurrentDB.Core.Exceptions;
using ILogger = Serilog.ILogger;
using MD5 = KurrentDB.Core.Hashing.MD5;
using Range = KurrentDB.Core.Data.Range;

namespace KurrentDB.Core.Index;

public class PTableVersions {
	// original
	public const byte IndexV1 = 1;

	// 64bit hashes
	public const byte IndexV2 = 2;

	// 64bit versions
	public const byte IndexV3 = 3;

	// cached midpoints
	public const byte IndexV4 = 4;
}

public partial class PTable : ISearchTable, IDisposable {
	public const int MD5Size = 16;
	public const int DefaultBufferSize = 8192;
	public const int DefaultSequentialBufferSize = 65536;
	private static readonly ILogger Log = Serilog.Log.ForContext<PTable>();

	public Guid Id {
		get { return _id; }
	}

	public long Count {
		get { return _count; }
	}

	public string Filename {
		get { return _filename; }
	}

	public byte Version {
		get { return _version; }
	}

	public string BloomFilterFilename => GenBloomFilterFilename(_filename);

	public bool HasBloomFilter => _bloomFilter is not null;

	public static string GenBloomFilterFilename(string filename) => $"{filename}.bloomfilter";

	private static long GenBloomFilterSizeBytes(long entryCount) {
		// Fewer events per stream will require a larger bloom filter (or incur more false positives)
		// We could count them to be precise, but a reasonable estimate will be faster.
		const int averageEventsPerStreamPerFile = 4;
		long size = entryCount / averageEventsPerStreamPerFile;
		size = Math.Clamp(
			value: size,
			min: BloomFilterAccessor.MinSizeKB * 1000,
			max: BloomFilterAccessor.MaxSizeKB * 1000);
		return size;
	}

	private readonly Guid _id;
	private readonly string _filename;
	private readonly long _count;
	private readonly long _size;
	private readonly UnmanagedMemoryAppendOnlyList<Midpoint> _midpoints = null;
	private readonly uint _midpointsCached = 0;
	private readonly long _midpointsCacheSize = 0;

	private readonly PersistentBloomFilter _bloomFilter;
	private readonly LRUCache<StreamHash, CacheEntry> _lruCache;
	private readonly LRUCache<StreamHash, bool> _lruConfirmedNotPresent;

	private readonly IndexEntryKey _minEntry, _maxEntry;
	private readonly ObjectPool<WorkItem> _workItems;
	private readonly byte _version;

	private readonly ManualResetEventSlim _destroyEvent = new ManualResetEventSlim(false);
	private volatile bool _deleteFile;
	private bool _disposed;

	public ReadOnlySpan<Midpoint> GetMidPoints() {
		if (_midpoints == null)
			return ReadOnlySpan<Midpoint>.Empty;

		return _midpoints.AsSpan();
	}

	private PTable(
		string filename,
		Guid id,
		int initialReaders,
		int maxReaders,
		int depth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {
		Ensure.NotNullOrEmpty(filename, "filename");
		Ensure.NotEmptyGuid(id, "id");
		Ensure.Positive(maxReaders, "maxReaders");
		Ensure.Nonnegative(depth, "depth");

		if (!File.Exists(filename))
			throw new CorruptIndexException(new PTableNotFoundException(filename));

		_id = id;
		_filename = filename;

		Log.Debug(skipIndexVerify
				? "Loading of PTable '{pTable}' started..."
				: "Loading and Verification of PTable '{pTable}' started...",
			Path.GetFileName(Filename)
		);
		var sw = Stopwatch.StartNew();
		_size = new FileInfo(_filename).Length;

		Helper.EatException(_filename, static filename => {
			// this action will fail if the file is created on a Unix system that does not have permissions to make files read-only
			File.SetAttributes(filename, FileAttributes.ReadOnly | FileAttributes.NotContentIndexed);
		});

		_workItems = new ObjectPool<WorkItem>(string.Format("PTable {0} work items", _id),
			initialReaders,
			maxReaders,
			() => new WorkItem(filename, DefaultBufferSize),
			workItem => workItem.Dispose(),
			pool => OnAllWorkItemsDisposed());

		int indexEntrySize;
		var readerWorkItem = GetWorkItem();
		try {
			var stream = readerWorkItem.Stream;
			stream.Seek(0, SeekOrigin.Begin);
			var header = PTableHeader.FromStream(stream);

			var version = header.Version;
			if (version == PTableVersions.IndexV1) {
				throw new CorruptIndexException(new UnsupportedFileVersionException(
					_filename, version, Version,
					"Detected a V1 index file, which is no longer supported. " +
					"The index will be backed up and rebuilt in a supported format. " +
					"This may take a long time for large databases. " +
					"You can also use a version of ESDB (>= 3.9.0 and < 24.10.0) to upgrade the " +
					"indexes to a supported format by performing an index merge."));
			}

			if (version is < PTableVersions.IndexV2 or > PTableVersions.IndexV4)
				throw new CorruptIndexException(new UnsupportedFileVersionException(_filename, version, Version));
			_version = version;
			indexEntrySize = GetIndexEntrySize(version);

			if (_version == PTableVersions.IndexV4) {
				//read the PTable footer
				stream.Seek(_size - MD5Size - PTableFooter.GetSize(_version), SeekOrigin.Begin);
				var footer = PTableFooter.FromStream(stream);
				if (footer.Version != version)
					throw new CorruptIndexException(
						String.Format("PTable header/footer version mismatch: {0}/{1}", version,
							footer.Version), new InvalidFileException("Invalid PTable file."));

				_midpointsCached = footer.NumMidpointsCached;
				_midpointsCacheSize = (long)_midpointsCached * Midpoint.Size;
			}

			long indexEntriesTotalSize = (_size - PTableHeader.Size - _midpointsCacheSize -
										  PTableFooter.GetSize(_version) - MD5Size);

			if (indexEntriesTotalSize < 0) {
				throw new CorruptIndexException(String.Format(
					"Total size of index entries < 0: {0}. _size: {1}, header size: {2}, _midpointsCacheSize: {3}, footer size: {4}, md5 size: {5}",
					indexEntriesTotalSize, _size, PTableHeader.Size, _midpointsCacheSize,
					PTableFooter.GetSize(_version), MD5Size));
			} else if (indexEntriesTotalSize % indexEntrySize != 0) {
				throw new CorruptIndexException(String.Format(
					"Total size of index entries: {0} is not divisible by index entry size: {1}",
					indexEntriesTotalSize, indexEntrySize));
			}

			_count = indexEntriesTotalSize / indexEntrySize;

			if (_version >= PTableVersions.IndexV4 && _count > 0 && _midpointsCached > 0 && _midpointsCached < 2) {
				//if there is at least 1 index entry with version>=4 and there are cached midpoints, there should always be at least 2 midpoints cached
				throw new CorruptIndexException(String.Format(
					"Less than 2 midpoints cached in PTable. Index entries: {0}, Midpoints cached: {1}", _count,
					_midpointsCached));
			} else if (_count >= 2 && _midpointsCached > _count) {
				//if there are at least 2 index entries, midpoints count should be at most the number of index entries
				throw new CorruptIndexException(String.Format(
					"More midpoints cached in PTable than index entries. Midpoints: {0} , Index entries: {1}",
					_midpointsCached, _count));
			}

			if (Count == 0) {
				_minEntry = new IndexEntryKey(ulong.MaxValue, long.MaxValue);
				_maxEntry = new IndexEntryKey(ulong.MinValue, long.MinValue);
			} else {
				var minEntry = ReadEntry(Count - 1, stream);
				_minEntry = minEntry.Key;
				var maxEntry = ReadEntry(0, stream);
				_maxEntry = maxEntry.Key;
			}
		} catch (Exception) {
			Dispose();
			throw;
		} finally {
			ReturnWorkItem(readerWorkItem);
		}

		var calcdepth = GetDepth(_count * indexEntrySize, depth);
		_midpoints = _version switch {
			>= PTableVersions.IndexV3 => CacheMidpointsAndVerifyHash<IndexEntry.V3>(calcdepth, skipIndexVerify),
			PTableVersions.IndexV2 => CacheMidpointsAndVerifyHash<IndexEntry.V2>(calcdepth, skipIndexVerify),
			_ => CacheMidpointsAndVerifyHash<IndexEntry.V1>(calcdepth, skipIndexVerify)
		};

		if (_midpoints != null) {
			// the bloom filter is important to the efficient functioning of the cache because without it
			// any cache miss request for data not contained in this file will cause two fruitless searches
			// to populate the _lruConfirmedNotPresent cache, which itself will become heavily used.
			if (lruCacheSize > 0 && !useBloomFilter) {
				Log.Warning("Index cache is enabled (--index-cache-size > 0) but will not be used because --use-index-bloom-filters is false");
			}

			if (useBloomFilter)
				_bloomFilter = TryOpenBloomFilter();

			if (lruCacheSize > 0) {
				if (_bloomFilter is not null) {
					_lruCache = new("ConfirmedPresent", lruCacheSize);
					_lruConfirmedNotPresent = new("ConfirmedNotPresent", lruCacheSize);
				} else {
					Log.Information("Not enabling LRU cache for index {file} because it has no bloom filter", _filename);
				}
			}
		} else {
			Log.Error(
				"Unable to create midpoints for PTable '{pTable}' ({count} entries, depth {depth} requested). "
				+ "Performance hit will occur. OOM Exception.", Path.GetFileName(Filename), Count, depth);
		}

		Log.Debug(
			"Loading PTable (Version: {version}) '{pTable}' ({count} entries, cache depth {depth}) done in {elapsed}.",
			_version, Path.GetFileName(Filename), Count, calcdepth, sw.Elapsed);
	}

	~PTable() => Dispose(false);

	private UnmanagedMemoryAppendOnlyList<Midpoint> CacheMidpointsAndVerifyHash<T>(int depth, bool skipIndexVerify)
		where T : struct, IndexEntry.ILayout<T> {
		if (depth < 0 || depth > 30)
			throw new ArgumentOutOfRangeException("depth");

		if (_count == 0 || depth == 0)
			return null;

		if (skipIndexVerify) {
			Log.Debug("Disabling Verification of PTable");
		}

		var workItem = GetWorkItem();
		var workItemStream = workItem.Stream;

		UnmanagedMemoryAppendOnlyList<Midpoint> midpoints = null;

		try {
			int midpointsCount = (int)Math.Max(2L, Math.Min((long)1 << depth, _count));
			try {
				midpoints = new UnmanagedMemoryAppendOnlyList<Midpoint>(midpointsCount);
			} catch (OutOfMemoryException) {
				return null;
			}

			if (skipIndexVerify && (_version >= PTableVersions.IndexV4)) {
				if (_midpointsCached == midpointsCount) {
					//index verification is disabled and cached midpoints with the same depth requested are available
					//so, we can load them directly from the PTable file
					ReadCachedMidpoints(workItemStream, midpoints);
					return midpoints;
				} else
					Log.Debug(
						"Skipping loading of cached midpoints from PTable due to count mismatch, cached midpoints: {midpointsCached} / required midpoints: {midpointsCount}",
						_midpointsCached, midpointsCount);
			}

			// Here, we construct midpoints from the index.
			// Two possible scenarios:
			// - Verification enabled: we scan the entire file and compute its hash, recording midpoints.
			// - Verification disabled: we just jump from midpoint to midpoint.
			workItemStream.Seek(0, SeekOrigin.Begin);

			if (skipIndexVerify) {
				CollectMidpoints<T>(midpointsCount, workItemStream, midpoints);
				return midpoints;
			}

			// Prevent BufferedStream from reading the hash part and affecting the calculated hash.
			using var segmentExcludingHash = new StreamSegment(workItemStream, leaveOpen: true) {
				Range = (Offset: 0, Length: _size - MD5Size)
			};

			using var md5 = MD5.Create();
			using var cryptoStream = new CryptoStream(segmentExcludingHash, md5, CryptoStreamMode.Read, leaveOpen: false);
			// Not PoolingBufferedStream because CryptoStream doesn't natively support Spans.
			// When `Read(Span<byte>)` is called, CryptoStream uses a temporary array internally
			// to read the data from the inner stream and then copies it to the provided span.
			using var stream = new BufferedStream(cryptoStream, DefaultSequentialBufferSize);

			byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
			try {
				long position = PTableHeader.Size; // CryptoStream doesn't support seeking, so we're tracking the position manually
				ReadUntil(delta: position, stream, buffer);
				CollectMidpointsWithVerification<T>(midpointsCount, ref position, stream, buffer, midpoints);

				ReadUntil(delta: _size - MD5Size - position, stream, buffer);
				cryptoStream.FlushFinalBlock();
				md5.TransformFinalBlock([], 0, 0);
			} finally {
				ArrayPool<byte>.Shared.Return(buffer);
			}

			// Reading from `workItemStream` because `stream` has the `hash` segment trimmed.
			Span<byte> fileHash = stackalloc byte[MD5Size];
			workItemStream.Seek(-MD5Size, SeekOrigin.End);
			workItemStream.ReadExactly(fileHash);
			ValidateHash(fileHash, md5.Hash);

			return midpoints;
		} catch {
			midpoints?.Dispose();
			Dispose();
			throw;
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	private void ReadCachedMidpoints(FileStream workItemStream, UnmanagedMemoryAppendOnlyList<Midpoint> midpoints) {
		Log.Debug("Loading {midpointsCached} cached midpoints from PTable", _midpointsCached);

		long startOffset = _size - MD5Size - PTableFooter.GetSize(_version) - _midpointsCacheSize;
		workItemStream.Seek(startOffset, SeekOrigin.Begin);

		for (int k = 0; k < (int)_midpointsCached; k++) {
			var midpoint = Midpoint.ReadFrom(workItemStream);
			midpoints.Add(midpoint);

			if (k > 0) {
				if (midpoints[k].Key.GreaterThan(midpoints[k - 1].Key)) {
					ThrowCorruptEntryKeyException(midpoints, k);
				} else if (midpoints[k - 1].ItemIndex > midpoints[k].ItemIndex) {
					ThrowCorruptMidpointIndexException(midpoints, k);
				}
			}
		}
	}

	private void CollectMidpoints<TEntry>(int midpointsCount, FileStream stream, UnmanagedMemoryAppendOnlyList<Midpoint> midpoints)
		where TEntry : struct, IndexEntry.ILayout<TEntry> {
		long previousNextIndex = long.MinValue;
		var previousKey = new IndexEntryKey(long.MaxValue, long.MaxValue);
		for (int k = 0; k < midpointsCount; ++k) {
			long nextIndex = GetMidpointIndex(k, _count, midpointsCount);
			if (previousNextIndex != nextIndex) {
				var nextPosition = PTableHeader.Size + TEntry.Size * nextIndex;
				stream.Seek(nextPosition, SeekOrigin.Begin);

				var entry = IndexEntry.ReadFrom<TEntry>(stream);

				midpoints.Add(new Midpoint(entry.Key, nextIndex));
				previousNextIndex = nextIndex;
				previousKey = entry.Key;
			} else {
				midpoints.Add(new Midpoint(previousKey, previousNextIndex));
			}

			if (k > 0) {
				if (midpoints[k].Key.GreaterThan(midpoints[k - 1].Key)) {
					ThrowCorruptEntryKeyException(midpoints, k);
				} else if (midpoints[k - 1].ItemIndex > midpoints[k].ItemIndex) {
					ThrowCorruptMidpointIndexException(midpoints, k);
				}
			}
		}
	}

	private void CollectMidpointsWithVerification<TEntry>(
		int midpointsCount,
		ref long position,
		Stream stream,
		byte[] buffer,
		UnmanagedMemoryAppendOnlyList<Midpoint> midpoints
	) where TEntry : struct, IndexEntry.ILayout<TEntry> {
		long previousNextIndex = long.MinValue;
		var previousKey = new IndexEntryKey(long.MaxValue, long.MaxValue);
		for (int k = 0; k < midpointsCount; ++k) {
			long nextIndex = GetMidpointIndex(k, _count, midpointsCount);
			if (previousNextIndex != nextIndex) {
				var nextPosition = PTableHeader.Size + TEntry.Size * nextIndex;
				ReadUntil(delta: nextPosition - position, stream, buffer);
				position = nextPosition;

				var entry = IndexEntry.ReadFrom<TEntry>(stream);
				position += TEntry.Size;

				midpoints.Add(new Midpoint(entry.Key, nextIndex));
				previousNextIndex = nextIndex;
				previousKey = entry.Key;
			} else {
				midpoints.Add(new Midpoint(previousKey, previousNextIndex));
			}

			if (k > 0) {
				if (midpoints[k].Key.GreaterThan(midpoints[k - 1].Key)) {
					ThrowCorruptEntryKeyException(midpoints, k);
				} else if (midpoints[k - 1].ItemIndex > midpoints[k].ItemIndex) {
					ThrowCorruptMidpointIndexException(midpoints, k);
				}
			}
		}
	}

	private static void ThrowCorruptEntryKeyException(UnmanagedMemoryAppendOnlyList<Midpoint> midpoints, int k) {
		throw new CorruptIndexException(String.Format(
			"Index entry key for midpoint {0} (stream: {1}, version: {2}) < index entry key for midpoint {3} (stream: {4}, version: {5})",
			k - 1,
			midpoints[k - 1].Key.Stream,
			midpoints[k - 1].Key.Version,
			k,
			midpoints[k].Key.Stream,
			midpoints[k].Key.Version
		));
	}

	private static void ThrowCorruptMidpointIndexException(UnmanagedMemoryAppendOnlyList<Midpoint> midpoints, int k) {
		throw new CorruptIndexException(String.Format(
			"Item index for midpoint {0} ({1}) > Item index for midpoint {2} ({3})",
			k - 1,
			midpoints[k - 1].ItemIndex,
			k,
			midpoints[k].ItemIndex
		));
	}

	private PersistentBloomFilter TryOpenBloomFilter() {
		try {
			// use existing filter without specifying what size it needs to be
			// for scavenged ptables in particular we do not know exactly what size the bloom filter
			// is because it is based on the pre-scavenge size
			var bloomFilter = new PersistentBloomFilter(
				FileStreamPersistence.FromFile(BloomFilterFilename));

			return bloomFilter;
		} catch (FileNotFoundException) {
			Log.Information("Bloom filter for index file {file} does not exist", _filename);
			return null;
		} catch (CorruptedFileException ex) {
			Log.Error(ex, "Bloom filter for index file {file} is corrupt. Performance will be degraded", _filename);
			return null;
		} catch (CorruptedHashException ex) {
			Log.Error(ex, "Bloom filter contents for index file {file} are corrupt. Performance will be degraded", _filename);
			return null;
		} catch (OutOfMemoryException ex) {
			Log.Warning(ex, "Could not allocate enough memory for Bloom filter for index file {file}. Performance will be degraded", _filename);
			return null;
		} catch (Exception ex) {
			Log.Error(ex, "Unexpected error opening bloom filter for index file {file}. Performance will be degraded", _filename);
			return null;
		}
	}

	private static void ReadUntil(long delta, Stream stream, byte[] buffer) {
		if (delta < 0)
			throw new Exception("should not do negative reads.");

		while (delta > 0) {
			var readBlockLength = Math.Min(delta, buffer.Length);
			stream.ReadExactly(buffer, 0, (int)readBlockLength);
			delta -= readBlockLength;
		}
	}

	private static void ValidateHash(Span<byte> fromFile, Span<byte> computed) {
		if (computed.IsEmpty)
			throw new CorruptIndexException(new HashValidationException("Calculated MD5 hash is empty"));
		if (fromFile.IsEmpty)
			throw new CorruptIndexException(new HashValidationException("Read from file MD5 hash is empty"));

		if (!fromFile.SequenceEqual(computed)) {
			throw new CorruptIndexException(
				new HashValidationException(
					string.Format(
						"Hashes are different! computed: {0}, hash: {1}.",
						Convert.ToHexString(computed),
						Convert.ToHexString(fromFile))));
		}
	}

	public IEnumerable<IndexEntry> IterateAllInOrder() {
		return _version switch {
			>= PTableVersions.IndexV3 => IterateAs<IndexEntry.V3>(this),
			PTableVersions.IndexV2 => IterateAs<IndexEntry.V2>(this),
			_ => IterateAs<IndexEntry.V1>(this)
		};

		static IEnumerable<IndexEntry> IterateAs<T>(PTable self) where T : struct, IndexEntry.ILayout<T> {
			var workItem = self.GetWorkItem();
			try {
				workItem.Stream.Position = PTableHeader.Size;

				for (long i = 0, n = self.Count; i < n; i++) {
					yield return IndexEntry.ReadFrom<T>(workItem.Stream);
				}
			} finally {
				self.ReturnWorkItem(workItem);
			}
		}
	}

	public bool TryGetOneValue(ulong stream, long number, out long position) {
		if (TryGetLatestEntryNoCache(GetHash(stream), number, number, out var entry)) {
			position = entry.Position;
			return true;
		}

		position = -1;
		return false;
	}

	public bool TryGetLatestEntry(ulong stream, out IndexEntry entry) =>
		_lruCache == null
			? TryGetLatestEntryNoCache(GetHash(stream), 0, long.MaxValue, out entry)
			: TryGetLatestEntryWithCache(GetHash(stream), out entry);

	private bool TryGetLatestEntryWithCache(StreamHash stream, out IndexEntry entry) {
		if (!TryLookThroughLru(stream, out var value)) {
			// stream not present
			entry = TableIndex.InvalidIndexEntry;
			return false;
		}

		entry = ReadEntry(value.LatestOffset);
		return true;
	}

	public ValueTask<IndexEntry?> TryGetLatestEntry(
		ulong stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		CancellationToken token) {
		return _version switch {
			>= PTableVersions.IndexV3 => TryGetLatestEntry<IndexEntry.V3>(stream, beforePosition, isForThisStream, token),
			PTableVersions.IndexV2 => TryGetLatestEntry<IndexEntry.V2>(stream, beforePosition, isForThisStream, token),
			_ => TryGetLatestEntry<IndexEntry.V1>(stream, beforePosition, isForThisStream, token)
		};
	}

	private async ValueTask<IndexEntry?> TryGetLatestEntry<T>(
		ulong stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		CancellationToken token)
		where T : struct, IndexEntry.ILayout<T> {

		Ensure.Nonnegative(beforePosition, nameof(beforePosition));
		var streamHash = GetHash(stream);

		var startKey = BuildKey(streamHash, 0);
		var endKey = BuildKey(streamHash, long.MaxValue);

		if (startKey.GreaterThan(_maxEntry) || endKey.SmallerThan(_minEntry))
			return null;

		if (!MightContainStream(streamHash))
			return null;

		var workItem = GetWorkItem();
		try {
			var recordRange = LocateRecordRange(endKey, startKey, out var lowBoundsCheck, out var highBoundsCheck);

			try {
				return await TryGetLatestEntryFast<T>(
					streamHash,
					beforePosition,
					isForThisStream,
					recordRange,
					lowBoundsCheck,
					highBoundsCheck,
					workItem,
					token);
			} catch (HashCollisionException) {
				// fall back to linear search if there's a hash collision
				return await TryGetLatestEntrySlow<T>(
					streamHash,
					beforePosition,
					isForThisStream,
					recordRange,
					lowBoundsCheck,
					highBoundsCheck,
					workItem,
					token);
			}
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	// linearly search the whole range for the entry with the greatest position that
	// is for this stream and before the beforePosition.
	private async ValueTask<IndexEntry?> TryGetLatestEntrySlow<T>(
		StreamHash stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		Range recordRange,
		IndexEntryKey lowBoundsCheck,
		IndexEntryKey highBoundsCheck,
		WorkItem workItem,
		CancellationToken token)
		where T : struct, IndexEntry.ILayout<T>{

		long maxBeforePosition = long.MinValue;
		IndexEntry maxEntry = default;

		for (var idx = recordRange.Lower; idx <= recordRange.Upper; idx++) {
			var candidateEntry = ReadEntry<T>(idx, workItem.Stream);
			var candidateEntryKey = candidateEntry.Key;

			if (candidateEntryKey.GreaterThan(lowBoundsCheck)) {
				throw new MaybeCorruptIndexException(
					$"Candidate entry key (stream: {candidateEntryKey.Stream}, version: {candidateEntryKey.Version}) > "
					+ $"low bounds check key (stream: {lowBoundsCheck.Stream}, version: {lowBoundsCheck.Version})");
			}

			if (candidateEntryKey.SmallerThan(highBoundsCheck)) {
				throw new MaybeCorruptIndexException(
					$"Candidate entry key (stream: {candidateEntryKey.Stream}, version: {candidateEntryKey.Version}) < "
					+ $"high bounds check key (stream: {highBoundsCheck.Stream}, version: {highBoundsCheck.Version})");
			}

			if (candidateEntry.Stream == stream.Hash &&
				candidateEntry.Position < beforePosition &&
				candidateEntry.Position > maxBeforePosition &&
				await isForThisStream(candidateEntry, token)) {

				maxBeforePosition = candidateEntry.Position;
				maxEntry = candidateEntry;
			}
		}

		return maxBeforePosition is not long.MinValue ? maxEntry : null;
	}

	private async ValueTask<IndexEntry?> TryGetLatestEntryFast<T>(
		StreamHash stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		Range recordRange,
		IndexEntryKey lowBoundsCheck,
		IndexEntryKey highBoundsCheck,
		WorkItem workItem,
		CancellationToken token)
		where T : struct, IndexEntry.ILayout<T>{

		var startKey = BuildKey(stream, 0);
		var endKey = BuildKey(stream, long.MaxValue);

		var low = recordRange.Lower;
		var high = recordRange.Upper;

		while (low < high) {
			var mid = low + (high - low) / 2;
			IndexEntry midpoint = ReadEntry<T>(mid, workItem.Stream);

			var midpointKey = midpoint.Key;
			if (midpointKey.GreaterThan(lowBoundsCheck)) {
				throw new MaybeCorruptIndexException(
					$"Midpoint key (stream: {midpointKey.Stream}, version: {midpointKey.Version}) > "
				+ $"low bounds check key (stream: {lowBoundsCheck.Stream}, version: {lowBoundsCheck.Version})");
			}

			if (midpointKey.SmallerThan(highBoundsCheck)) {
				throw new MaybeCorruptIndexException(
					$"Midpoint key (stream: {midpointKey.Stream}, version: {midpointKey.Version}) < "
				+ $"high bounds check key (stream: {highBoundsCheck.Stream}, version: {highBoundsCheck.Version})");
			}

			if (midpointKey.Stream != stream.Hash) {
				if (midpointKey.GreaterThan(endKey)) {
					low = mid + 1;
					lowBoundsCheck = midpointKey;
				} else if (midpointKey.SmallerThan(startKey)) {
					high = mid - 1;
					highBoundsCheck = midpointKey;
				} else
					throw new MaybeCorruptIndexException(
					$"Midpoint key (stream: {midpointKey.Stream}, version: {midpointKey.Version}) >= "
					+ $"start key (stream: {startKey.Stream}, version: {startKey.Version}) and <= "
					+ $"end key (stream: {endKey.Stream}, version: {endKey.Version}) "
					+ "but the stream hashes do not match.");
				continue;
			}

			if (!await isForThisStream(midpoint, token))
				throw new HashCollisionException();

			if (midpoint.Position >= beforePosition) {
				low = mid + 1;
				lowBoundsCheck = midpointKey;
			} else {
				high = mid;
				highBoundsCheck = midpointKey;
			}
		}

		var candidateEntry = ReadEntry<T>(high, workItem.Stream);

		// index entry is for a different hash
		if (candidateEntry.Stream != stream.Hash)
			return null;

		// index entry is for the correct hash but for a colliding stream
		if (!await isForThisStream(candidateEntry, token))
			throw new HashCollisionException();

		// index entry is for the correct stream but does not respect the position limit
		if (candidateEntry.Position >= beforePosition) {
			return null;
		}

		// index entry is for the correct stream and respects the position limit
		return candidateEntry;
	}

	private bool TryGetLatestEntryNoCache(StreamHash stream, long startNumber, long endNumber, out IndexEntry entry) {
		Ensure.Nonnegative(startNumber, "startNumber");
		Ensure.Nonnegative(endNumber, "endNumber");

		if (!MightContainStream(stream)) {
			entry = TableIndex.InvalidIndexEntry;
			return false;
		}

		return TrySearchForLatestEntry(stream, startNumber, endNumber, out entry, out _);
	}

	private bool TrySearchForLatestEntry(StreamHash stream, long startNumber, long endNumber,
		out IndexEntry entry, out long offset) {

		entry = TableIndex.InvalidIndexEntry;

		var startKey = BuildKey(stream, startNumber);
		var endKey = BuildKey(stream, endNumber);

		if (startKey.GreaterThan(_maxEntry) || endKey.SmallerThan(_minEntry)) {
			offset = default;
			return false;
		}

		var workItem = GetWorkItem();
		try {
			var high = ChopForLatest(workItem, endKey);
			var candEntry = ReadEntry(high, workItem.Stream);
			var candKey = candEntry.Key;

			if (candKey.GreaterThan(endKey))
				throw new MaybeCorruptIndexException(string.Format(
					"candEntry ({0}@{1}) > startKey {2}, stream {3}, startNum {4}, endNum {5}, PTable: {6}.",
					candEntry.Stream, candEntry.Version, startKey, stream, startNumber, endNumber, Filename));
			if (candKey.SmallerThan(startKey)) {
				offset = default;
				return false;
			}

			entry = candEntry;
			offset = high;
			return true;
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	public bool TryGetOldestEntry(ulong stream, out IndexEntry entry) =>
		_lruCache == null
			? TryGetOldestEntryNoCache(GetHash(stream), out entry)
			: TryGetOldestEntryWithCache(GetHash(stream), out entry);

	private bool TryGetOldestEntryWithCache(StreamHash stream, out IndexEntry entry) {
		if (!TryLookThroughLru(stream, out var value)) {
			// stream not present
			entry = TableIndex.InvalidIndexEntry;
			return false;
		}

		entry = ReadEntry(value.OldestOffset);
		return true;
	}

	private bool TryGetOldestEntryNoCache(StreamHash stream, out IndexEntry entry) {
		if (!MightContainStream(stream)) {
			entry = TableIndex.InvalidIndexEntry;
			return false;
		}

		return TrySearchForOldestEntry(stream, 0, long.MaxValue, out entry, out _);
	}

	public bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry) {
		var hash = GetHash(stream);
		if (afterVersion >= long.MaxValue || !MightContainStream(hash)) {
			entry = TableIndex.InvalidIndexEntry;
			return false;
		}
		return TrySearchForOldestEntry(hash, afterVersion + 1, long.MaxValue, out entry, out _);
	}

	public bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry) {
		var hash = GetHash(stream);
		if (beforeVersion <= 0 || !MightContainStream(hash)) {
			entry = TableIndex.InvalidIndexEntry;
			return false;
		}
		return TrySearchForLatestEntry(hash, 0, beforeVersion - 1, out entry, out _);
	}

	private bool TrySearchForOldestEntry(StreamHash stream, long startNumber, long endNumber,
		out IndexEntry entry, out long offset) {
		Ensure.Nonnegative(startNumber, "startNumber");
		Ensure.Nonnegative(endNumber, "endNumber");

		entry = TableIndex.InvalidIndexEntry;

		var startKey = BuildKey(stream, startNumber);
		var endKey = BuildKey(stream, endNumber);

		if (startKey.GreaterThan(_maxEntry) || endKey.SmallerThan(_minEntry)) {
			offset = default;
			return false;
		}

		var workItem = GetWorkItem();
		try {
			var high = ChopForOldest(workItem, startKey);
			var candEntry = ReadEntry(high, workItem.Stream);
			var candidateKey = candEntry.Key;
			if (candidateKey.SmallerThan(startKey))
				throw new MaybeCorruptIndexException(string.Format(
					"candEntry ({0}@{1}) < startKey {2}, stream {3}, startNum {4}, endNum {5}, PTable: {6}.",
					candEntry.Stream, candEntry.Version, startKey, stream, startNumber, endNumber, Filename));
			if (candidateKey.GreaterThan(endKey)) {
				offset = default;
				return false;
			}

			entry = candEntry;
			offset = high;
			return true;
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	public IReadOnlyList<IndexEntry> GetRange(ulong stream, long startNumber, long endNumber, int? limit = null) {
		Ensure.Nonnegative(startNumber, "startNumber");
		Ensure.Nonnegative(endNumber, "endNumber");

		return _lruCache is null
			? GetRangeNoCache(GetHash(stream), startNumber, endNumber, limit)
			: GetRangeWithCache(GetHash(stream), startNumber, endNumber, limit);
	}

	private StreamHash GetHash(ulong hash) {
		return new(_version, hash);
	}

	private static IndexEntryKey BuildKey(StreamHash stream, long version) {
		return new IndexEntryKey(stream.Hash, version);
	}

	// use the midpoints (if they exist) to narrow the search range.
	// returns a range of indexes to search and corresponding IndexEntryKeys
	private Range LocateRecordRange(IndexEntryKey key, out IndexEntryKey lowKey, out IndexEntryKey highKey) =>
		LocateRecordRange(key, key, out lowKey, out highKey);

	private Range LocateRecordRange(IndexEntryKey lowKey, IndexEntryKey highKey, out IndexEntryKey lowKeyOut, out IndexEntryKey highKeyOut) {
		lowKeyOut = new IndexEntryKey(ulong.MaxValue, long.MaxValue);
		highKeyOut = new IndexEntryKey(ulong.MinValue, long.MinValue);

		ReadOnlySpan<Midpoint> midpoints = default;
		if (_midpoints != null) {
			midpoints = _midpoints.AsSpan();
		}

		if (midpoints.IsEmpty)
			return new Range(0, Count - 1);

		long lowerMidpoint = LowerMidpointBound(midpoints, lowKey);
		long upperMidpoint = UpperMidpointBound(midpoints, highKey);

		lowKeyOut = midpoints[(int)lowerMidpoint].Key;
		highKeyOut = midpoints[(int)upperMidpoint].Key;

		return new Range(midpoints[(int)lowerMidpoint].ItemIndex, midpoints[(int)upperMidpoint].ItemIndex);
	}

	private long LowerMidpointBound(ReadOnlySpan<Midpoint> midpoints, IndexEntryKey key) {
		long l = 0;
		long r = midpoints.Length - 1;
		while (l < r) {
			long m = l + (r - l + 1) / 2;
			if (midpoints[(int)m].Key.GreaterThan(key))
				l = m;
			else
				r = m - 1;
		}

		return l;
	}

	private long UpperMidpointBound(ReadOnlySpan<Midpoint> midpoints, IndexEntryKey key) {
		long l = 0;
		long r = midpoints.Length - 1;
		while (l < r) {
			long m = l + (r - l) / 2;
			if (midpoints[(int)m].Key.SmallerThan(key))
				r = m;
			else
				l = m + 1;
		}

		return r;
	}

	private IndexEntry ReadEntry(long indexNum, FileStream stream) {
		return _version switch {
			>= PTableVersions.IndexV3 => ReadEntry<IndexEntry.V3>(indexNum, stream),
			PTableVersions.IndexV2 => ReadEntry<IndexEntry.V2>(indexNum, stream),
			_ => ReadEntry<IndexEntry.V1>(indexNum, stream)
		};
	}

	private static IndexEntry ReadEntry<T>(long indexNum, Stream stream)
		where T : struct, IndexEntry.ILayout<T> {

		long seekTo = T.Size * indexNum + PTableHeader.Size;
		stream.Seek(seekTo, SeekOrigin.Begin);
		return IndexEntry.ReadFrom<T>(stream);
	}

	private IndexEntry ReadEntry(long indexNum) {
		var workItem = GetWorkItem();
		try {
			return ReadEntry(indexNum, workItem.Stream);
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	private WorkItem GetWorkItem() {
		try {
			return _workItems.Get();
		} catch (ObjectPoolDisposingException) {
			throw new FileBeingDeletedException();
		} catch (ObjectPoolMaxLimitReachedException) {
			throw new Exception("Unable to acquire work item.");
		}
	}

	private void ReturnWorkItem(WorkItem workItem) {
		_workItems.Return(workItem);
	}

	public void MarkForDestruction() {
		_deleteFile = true;
		_workItems.MarkForDisposal();
	}

	public void Dispose() {
		_deleteFile = false;
		_workItems.MarkForDisposal();
	}

	protected virtual void Dispose(bool disposing) {
		if (_disposed) {
			return;
		}

		if (disposing) {
			//dispose any managed objects here
			_midpoints?.Dispose();
			_bloomFilter?.Dispose();
		}

		_disposed = true;
	}

	private void OnAllWorkItemsDisposed() {
		File.SetAttributes(_filename, FileAttributes.Normal);
		if (_deleteFile) {
			_bloomFilter?.Dispose();
			File.Delete(_filename);
			File.Delete(BloomFilterFilename);
		}
		_destroyEvent.Set();
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	public void WaitForDisposal(int timeout) {
		if (!_destroyEvent.Wait(timeout))
			throw new TimeoutException();
	}

	public void WaitForDisposal(TimeSpan timeout) {
		if (!_destroyEvent.Wait(timeout))
			throw new TimeoutException();
	}

	public List<IndexEntry> GetRangeWithCache(StreamHash stream, long startNumber, long endNumber, int? limit = null) {
		if (!OverlapsRange(stream, startNumber, endNumber, out var tableLatestNumber, out var tableLatestOffset))
			return new List<IndexEntry>();

		// it does overlap.
		// if the requested end version is greater than or equal to what we have in this ptable
		// then we can jump to tableLatestOffset and read the file forwards from there without binary chopping.
		if (endNumber >= tableLatestNumber) {
			return PositionAndReadForward(stream, startNumber, endNumber, limit, tableLatestOffset: tableLatestOffset);
		}
		// todo: else if the requested start version is less than or equal to what we have in this ptable
		// then we could jump to tableStartOffset and read the file backwards.

		// otherwise the request is contained strictly within what we have in this ptable
		// and we must chop for it
		return ChopAndReadForward(stream, startNumber, endNumber, limit);
	}

	public List<IndexEntry> GetRangeNoCache(StreamHash stream, long startNumber, long endNumber, int? limit = null) {
		if (!MightContainStream(stream))
			return new List<IndexEntry>();

		return ChopAndReadForward(stream, startNumber, endNumber, limit);
	}

	private List<IndexEntry> ChopAndReadForward(StreamHash stream, long startNumber, long endNumber, int? limit) {
		return PositionAndReadForward(stream, startNumber, endNumber, limit: limit, tableLatestOffset: null);
	}

	private List<IndexEntry> PositionAndReadForward(StreamHash stream, long startNumber, long endNumber, int? limit, long? tableLatestOffset) {
		var result = new List<IndexEntry>();

		var startKey = BuildKey(stream, startNumber);
		var endKey = BuildKey(stream, endNumber);

		if (startKey.GreaterThan(_maxEntry) || endKey.SmallerThan(_minEntry))
			return result;

		var workItem = GetWorkItem();
		try {
			var high = tableLatestOffset ?? ChopForLatest(workItem, endKey);
			result = _version switch {
				>= PTableVersions.IndexV3 => ReadForward<IndexEntry.V3>(workItem.Stream, high, startKey, endKey, limit),
				PTableVersions.IndexV2 => ReadForward<IndexEntry.V2>(workItem.Stream, high, startKey, endKey, limit),
				_ => ReadForward<IndexEntry.V1>(workItem.Stream, high, startKey, endKey, limit)
			};
			return result;
		} catch (MaybeCorruptIndexException ex) {
			throw new MaybeCorruptIndexException(
				$"{ex.Message}. stream {stream}, startNum {startNumber}, endNum {endNumber}, PTable: {Filename}.");
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	// forward here meaning forward in the file. towards the older records.
	private List<IndexEntry> ReadForward<T>(Stream stream, long high, IndexEntryKey startKey, IndexEntryKey endKey, int? limit)
		where T : struct, IndexEntry.ILayout<T> {

		stream.Seek(T.Size * high + PTableHeader.Size, SeekOrigin.Begin);

		var result = new List<IndexEntry>();
		for (long i = high, n = Count; i < n; ++i) {
			var entry = IndexEntry.ReadFrom<T>(stream);

			var candidateKey = entry.Key;

			if (candidateKey.GreaterThan(endKey))
				throw new MaybeCorruptIndexException($"candidateKey ({candidateKey}) > endKey ({endKey})");

			if (candidateKey.SmallerThan(startKey))
				return result;

			result.Add(entry);

			if (result.Count == limit)
				break;
		}

		return result;
	}

	private long ChopForLatest(WorkItem workItem, in IndexEntryKey endKey) {
		return _version switch {
			>= PTableVersions.IndexV3 => ChopForLatest<IndexEntry.V3>(workItem, endKey),
			PTableVersions.IndexV2 => ChopForLatest<IndexEntry.V2>(workItem, endKey),
			_ => ChopForLatest<IndexEntry.V1>(workItem, endKey)
		};
	}

	private long ChopForLatest<T>(WorkItem workItem, in IndexEntryKey endKey) where T : struct, IndexEntry.ILayout<T> {
		var recordRange = LocateRecordRange(endKey, out var lowBoundsCheck, out var highBoundsCheck);
		long low = recordRange.Lower;
		long high = recordRange.Upper;
		while (low < high) {
			var mid = low + (high - low) / 2;
			var midpoint = ReadEntry<T>(mid, workItem.Stream);
			var midpointKey = midpoint.Key;

			if (midpointKey.GreaterThan(lowBoundsCheck)) {
				throw new MaybeCorruptIndexException(String.Format(
					"Midpoint key (stream: {0}, version: {1}) > low bounds check key (stream: {2}, version: {3})",
					midpointKey.Stream, midpointKey.Version, lowBoundsCheck.Stream, lowBoundsCheck.Version));
			} else if (!midpointKey.GreaterEqualsThan(highBoundsCheck)) {
				throw new MaybeCorruptIndexException(String.Format(
					"Midpoint key (stream: {0}, version: {1}) < high bounds check key (stream: {2}, version: {3})",
					midpointKey.Stream, midpointKey.Version, highBoundsCheck.Stream, highBoundsCheck.Version));
			}

			if (midpointKey.GreaterThan(endKey)) {
				low = mid + 1;
				lowBoundsCheck = midpointKey;
			} else {
				high = mid;
				highBoundsCheck = midpointKey;
			}
		}

		return high;
	}

	private long ChopForOldest(WorkItem workItem, IndexEntryKey startKey) {
		return _version switch {
			>= PTableVersions.IndexV3 => ChopForOldest<IndexEntry.V3>(workItem, startKey),
			PTableVersions.IndexV2 => ChopForOldest<IndexEntry.V2>(workItem, startKey),
			_ => ChopForOldest<IndexEntry.V1>(workItem, startKey)
		};
	}


	private long ChopForOldest<T>(WorkItem workItem, IndexEntryKey startKey) where T : struct, IndexEntry.ILayout<T> {
		var recordRange = LocateRecordRange(startKey, out var lowBoundsCheck, out var highBoundsCheck);
		long low = recordRange.Lower;
		long high = recordRange.Upper;
		while (low < high) {
			var mid = low + (high - low + 1) / 2;
			var midpoint = ReadEntry<T>(mid, workItem.Stream);
			var midpointKey = midpoint.Key;

			if (midpointKey.GreaterThan(lowBoundsCheck)) {
				throw new MaybeCorruptIndexException(String.Format(
					"Midpoint key (stream: {0}, version: {1}) > low bounds check key (stream: {2}, version: {3})",
					midpointKey.Stream, midpointKey.Version, lowBoundsCheck.Stream, lowBoundsCheck.Version));
			} else if (!midpointKey.GreaterEqualsThan(highBoundsCheck)) {
				throw new MaybeCorruptIndexException(String.Format(
					"Midpoint key (stream: {0}, version: {1}) < high bounds check key (stream: {2}, version: {3})",
					midpointKey.Stream, midpointKey.Version, highBoundsCheck.Stream, highBoundsCheck.Version));
			}

			if (midpointKey.SmallerThan(startKey)) {
				high = mid - 1;
				highBoundsCheck = midpointKey;
			} else {
				low = mid;
				lowBoundsCheck = midpointKey;
			}
		}

		return high;
	}

	// Checks if this file might contain any of the range from start to end inclusive.
	private bool OverlapsRange(
		StreamHash stream,
		long startNumber,
		long endNumber,
		out long tableLatestNumber,
		out long tableLatestOffset) {

		if (!TryLookThroughLru(stream, out var cacheEntry)) {
			// no range present
			tableLatestNumber = default;
			tableLatestOffset = default;
			return false;
		}

		tableLatestNumber = cacheEntry.LatestNumber;
		tableLatestOffset = cacheEntry.LatestOffset;

		// there is a range for this stream, does it overlap?
		return startNumber <= cacheEntry.LatestNumber && cacheEntry.OldestNumber <= endNumber;
	}

	// Gets the value from the lru cache. Populate the cache if necessary
	// returns true iff we managed to get a CacheEntry. i.e. if any events are
	// present for this stream in this file.
	private bool TryLookThroughLru(StreamHash stream, out CacheEntry value) {
		Ensure.NotNull(_lruCache, nameof(_lruCache));

		if (_lruCache.TryGet(stream, out value)) {
			return true;
		}

		if (!MightContainStream(stream)) {
			value = default;
			return false;
		}

		if (_lruConfirmedNotPresent.TryGet(stream, out _)) {
			value = default;
			return false;
		}

		// its not in either of the LRU caches. add it to one or the other
		// so that subsequent calls do not require searching.
		if (TrySearchForLatestEntry(stream, 0, long.MaxValue, out var latestEntry, out var latestOffset) &&
			TrySearchForOldestEntry(stream, 0, long.MaxValue, out var oldestEntry, out var oldestOffset)) {

			value = new(
				oldestNumber: oldestEntry.Version,
				latestNumber: latestEntry.Version,
				oldestOffset: oldestOffset,
				latestOffset: latestOffset);

			_lruCache.Put(stream, value);
			return true;
		} else {
			// in case of false positive in the bloom filter
			_lruConfirmedNotPresent.Put(stream, true);
			value = default;
			return false;
		}
	}

	private bool MightContainStream(StreamHash stream) {
		if (_bloomFilter == null)
			return true;

		// with a workitem checked out the ptable (and bloom filter specifically)
		// wont get disposed
		var workItem = GetWorkItem();
		try {
			var streamHash = stream.Hash;
			return _bloomFilter.MightContain(GetSpan(ref streamHash));
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	private static ReadOnlySpan<byte> GetSpan(ref ulong streamHash) =>
		MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref streamHash, 1));

	internal static int GetIndexEntrySize(byte ptableVersion) {
		return ptableVersion switch {
			>= PTableVersions.IndexV3 => IndexEntry.V3.Size,
			PTableVersions.IndexV2 => IndexEntry.V2.Size,
			_ => IndexEntry.V1.Size
		};
	}

	internal static IndexEntry ReadIndexEntryFrom(Stream stream, byte ptableVersion) {
		return ptableVersion switch {
			>= PTableVersions.IndexV3 => IndexEntry.ReadFrom<IndexEntry.V3>(stream),
			PTableVersions.IndexV2 => IndexEntry.ReadFrom<IndexEntry.V2>(stream),
			_ => IndexEntry.ReadFrom<IndexEntry.V1>(stream)
		};
	}

	internal static void AppendIndexEntryTo(Stream stream, in IndexEntry entry, byte ptableVersion) {
		if (ptableVersion <= PTableVersions.IndexV2) {
			if (ptableVersion == PTableVersions.IndexV2) {
				entry.AppendTo<IndexEntry.V2>(stream);
			} else {
				entry.AppendTo<IndexEntry.V1>(stream);
			}
			return;
		}
		entry.AppendTo<IndexEntry.V3>(stream);
	}

	// construct this struct with a 64 bit hash and it will convert it to a hash
	// for the specified table version
	public readonly struct StreamHash : IEquatable<StreamHash> {
		public StreamHash(byte version, ulong hash) {
			Hash = version == PTableVersions.IndexV1 ? hash >> 32 : hash;
		}

		public ulong Hash { get; init; }

		public override int GetHashCode() =>
			Hash.GetHashCode();

		public bool Equals(StreamHash other) =>
			Hash == other.Hash;

		public override bool Equals(object obj) =>
			obj is StreamHash streamHash && Equals(streamHash);
	}

	public readonly struct CacheEntry {
		public readonly long OldestNumber;
		public readonly long LatestNumber;
		public readonly long OldestOffset;
		public readonly long LatestOffset;

		public CacheEntry(long oldestNumber, long latestNumber, long oldestOffset, long latestOffset) {
			OldestNumber = oldestNumber;
			LatestNumber = latestNumber;
			OldestOffset = oldestOffset;
			LatestOffset = latestOffset;
		}
	}

	[StructLayout(LayoutKind.Explicit)]
	public readonly struct Midpoint(ulong stream, long version, long itemIndex) {
		public const int Size = 24;

		[FieldOffset(0)] public readonly long Version = version;
		[FieldOffset(8)] public readonly ulong Stream = stream;
		[FieldOffset(16)] public readonly long ItemIndex = itemIndex;

		public IndexEntryKey Key => new(Stream, Version);

		public Midpoint(IndexEntryKey key, long itemIndex) : this(key.Stream, key.Version, itemIndex) {
		}

		[SkipLocalsInit]
		public void AppendTo(Stream stream) {
			var buffer = MemoryMarshal.AsReadOnlyBytes(in this);
			Debug.Assert(buffer.Length == Size);
			stream.Write(buffer);
		}

		[SkipLocalsInit]
		public static Midpoint ReadFrom(Stream input) {
			Debug.Assert(Unsafe.SizeOf<Midpoint>() == Size);
			Span<byte> buffer = stackalloc byte[Size];
			input.ReadExactly(buffer);
			return MemoryMarshal.Read<Midpoint>(buffer);
		}
	}

	public readonly struct IndexEntryKey {
		public readonly ulong Stream;
		public readonly long Version;

		public IndexEntryKey(ulong stream, long version) {
			Stream = stream;
			Version = version;
		}

		public bool GreaterThan(IndexEntryKey other) {
			if (Stream == other.Stream) {
				return Version > other.Version;
			}

			return Stream > other.Stream;
		}

		public bool SmallerThan(IndexEntryKey other) {
			if (Stream == other.Stream) {
				return Version < other.Version;
			}

			return Stream < other.Stream;
		}

		public bool GreaterEqualsThan(IndexEntryKey other) {
			if (Stream == other.Stream) {
				return Version >= other.Version;
			}

			return Stream >= other.Stream;
		}

		public bool SmallerEqualsThan(IndexEntryKey other) {
			if (Stream == other.Stream) {
				return Version <= other.Version;
			}

			return Stream <= other.Stream;
		}

		public override string ToString() {
			return $"Stream: {Stream}, Version: {Version}";
		}
	}

	private class WorkItem : IDisposable {
		public readonly FileStream Stream;

		public WorkItem(string filename, int bufferSize) {
			Stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
				FileOptions.RandomAccess);
		}

		public void Dispose() {
			Stream.Dispose();
		}
	}
}

internal class HashCollisionException : Exception {
}
