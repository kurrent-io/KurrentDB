// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.DataStructures;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.Unbuffered;
using ILogger = Serilog.ILogger;
using Range = EventStore.Core.Data.Range;
using EventStore.Core.DataStructures.ProbabilisticFilter;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using MD5 = EventStore.Core.Hashing.MD5;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace EventStore.Core.Index;

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
	public const int IndexEntryV1Size = sizeof(int) + sizeof(int) + sizeof(long);
	public const int IndexEntryV2Size = sizeof(int) + sizeof(long) + sizeof(long);
	public const int IndexEntryV3Size = sizeof(long) + sizeof(long) + sizeof(long);
	public const int IndexEntryV4Size = IndexEntryV3Size;

	public const int IndexKeyV1Size = sizeof(int) + sizeof(int);
	public const int IndexKeyV2Size = sizeof(int) + sizeof(long);
	public const int IndexKeyV3Size = sizeof(long) + sizeof(long);
	public const int IndexKeyV4Size = IndexKeyV3Size;
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
	private readonly int _indexEntrySize;
	private readonly int _indexKeySize;

	private readonly ManualResetEventSlim _destroyEvent = new ManualResetEventSlim(false);
	private volatile bool _deleteFile;
	private bool _disposed;

	public ReadOnlySpan<Midpoint> GetMidPoints() {
		if(_midpoints == null)
			return ReadOnlySpan<Midpoint>.Empty;

		return _midpoints.AsSpan();
	}

	private PTable(string filename,
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

		Log.Debug("Loading " + (skipIndexVerify ? "" : "and Verification ") + "of PTable '{pTable}' started...",
			Path.GetFileName(Filename));
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

		var readerWorkItem = GetWorkItem();
		try {
			readerWorkItem.Stream.Seek(0, SeekOrigin.Begin);
			var header = PTableHeader.FromStream(readerWorkItem.Stream);

			if (header.Version == PTableVersions.IndexV1) {
				throw new CorruptIndexException(new UnsupportedFileVersionException(
					_filename, header.Version, Version,
					"Detected a V1 index file, which is no longer supported. " +
					"The index will be backed up and rebuilt in a supported format. " +
					"This may take a long time for large databases. " +
					"You can also use a version of ESDB (>= 3.9.0 and < 24.10.0) to upgrade the " +
					"indexes to a supported format by performing an index merge."));
			}

			if ((header.Version != PTableVersions.IndexV2) &&
				(header.Version != PTableVersions.IndexV3) &&
				(header.Version != PTableVersions.IndexV4))
				throw new CorruptIndexException(new UnsupportedFileVersionException(_filename, header.Version, Version));
			_version = header.Version;

			if (_version == PTableVersions.IndexV1) {
				_indexEntrySize = IndexEntryV1Size;
				_indexKeySize = IndexKeyV1Size;
			}

			if (_version == PTableVersions.IndexV2) {
				_indexEntrySize = IndexEntryV2Size;
				_indexKeySize = IndexKeyV2Size;
			}

			if (_version == PTableVersions.IndexV3) {
				_indexEntrySize = IndexEntryV3Size;
				_indexKeySize = IndexKeyV3Size;
			}

			if (_version >= PTableVersions.IndexV4) {
				//read the PTable footer
				var previousPosition = readerWorkItem.Stream.Position;
				readerWorkItem.Stream.Seek(readerWorkItem.Stream.Length - MD5Size - PTableFooter.GetSize(_version),
					SeekOrigin.Begin);
				var footer = PTableFooter.FromStream(readerWorkItem.Stream);
				if (footer.Version != header.Version)
					throw new CorruptIndexException(
						String.Format("PTable header/footer version mismatch: {0}/{1}", header.Version,
							footer.Version), new InvalidFileException("Invalid PTable file."));

				if (_version == PTableVersions.IndexV4) {
					_indexEntrySize = IndexEntryV4Size;
					_indexKeySize = IndexKeyV4Size;
				} else
					throw new InvalidOperationException("Unknown PTable version: " + _version);

				_midpointsCached = footer.NumMidpointsCached;
				_midpointsCacheSize = _midpointsCached * _indexEntrySize;
				readerWorkItem.Stream.Seek(previousPosition, SeekOrigin.Begin);
			}

			long indexEntriesTotalSize = (_size - PTableHeader.Size - _midpointsCacheSize -
										  PTableFooter.GetSize(_version) - MD5Size);

			if (indexEntriesTotalSize < 0) {
				throw new CorruptIndexException(String.Format(
					"Total size of index entries < 0: {0}. _size: {1}, header size: {2}, _midpointsCacheSize: {3}, footer size: {4}, md5 size: {5}",
					indexEntriesTotalSize, _size, PTableHeader.Size, _midpointsCacheSize,
					PTableFooter.GetSize(_version), MD5Size));
			} else if (indexEntriesTotalSize % _indexEntrySize != 0) {
				throw new CorruptIndexException(String.Format(
					"Total size of index entries: {0} is not divisible by index entry size: {1}",
					indexEntriesTotalSize, _indexEntrySize));
			}

			_count = indexEntriesTotalSize / _indexEntrySize;

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
				var minEntry = ReadEntry(_indexEntrySize, Count - 1, readerWorkItem, _version);
				_minEntry = new IndexEntryKey(minEntry.Stream, minEntry.Version);
				var maxEntry = ReadEntry(_indexEntrySize, 0, readerWorkItem, _version);
				_maxEntry = new IndexEntryKey(maxEntry.Stream, maxEntry.Version);
			}
		} catch (Exception) {
			Dispose();
			throw;
		} finally {
			ReturnWorkItem(readerWorkItem);
		}

		int calcdepth = 0;
		try {
			calcdepth = GetDepth(_count * _indexEntrySize, depth);
			_midpoints = CacheMidpointsAndVerifyHash(calcdepth, skipIndexVerify);

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
		} catch (PossibleToHandleOutOfMemoryException) {
			Log.Error(
				"Unable to create midpoints for PTable '{pTable}' ({count} entries, depth {depth} requested). "
				+ "Performance hit will occur. OOM Exception.", Path.GetFileName(Filename), Count, depth);
		}

		Log.Debug(
			"Loading PTable (Version: {version}) '{pTable}' ({count} entries, cache depth {depth}) done in {elapsed}.",
			_version, Path.GetFileName(Filename), Count, calcdepth, sw.Elapsed);
	}

	~PTable() => Dispose(false);

	internal UnmanagedMemoryAppendOnlyList<Midpoint> CacheMidpointsAndVerifyHash(int depth, bool skipIndexVerify) {
		var buffer = new byte[4096];
		if (depth < 0 || depth > 30)
			throw new ArgumentOutOfRangeException("depth");
		var count = Count;
		if (count == 0 || depth == 0)
			return null;

		if (skipIndexVerify) {
			Log.Debug("Disabling Verification of PTable");
		}

		Stream stream = null;
		WorkItem workItem = null;
		if (RuntimeInformation.IsUnix) {
			workItem = GetWorkItem();
			stream = workItem.Stream;
		} else {
			stream = UnbufferedFileStream.Create(_filename, FileMode.Open, FileAccess.Read, FileShare.Read,
				4096, 4096, false, 4096);
		}

		UnmanagedMemoryAppendOnlyList<Midpoint> midpoints = null;

		try {
			using (var md5 = MD5.Create()) {
				int midpointsCount;
				try {
					midpointsCount = (int)Math.Max(2L, Math.Min((long)1 << depth, count));
					midpoints = new UnmanagedMemoryAppendOnlyList<Midpoint>(midpointsCount);
				} catch (OutOfMemoryException exc) {
					throw new PossibleToHandleOutOfMemoryException("Failed to allocate memory for Midpoint cache.",
						exc);
				}

				if (skipIndexVerify && (_version >= PTableVersions.IndexV4)) {
					if (_midpointsCached == midpointsCount) {
						//index verification is disabled and cached midpoints with the same depth requested are available
						//so, we can load them directly from the PTable file
						Log.Debug("Loading {midpointsCached} cached midpoints from PTable", _midpointsCached);
						long startOffset = stream.Length - MD5Size - PTableFooter.GetSize(_version) -
										   _midpointsCacheSize;
						stream.Seek(startOffset, SeekOrigin.Begin);
						for (int k = 0; k < (int)_midpointsCached; k++) {
							stream.Read(buffer, 0, _indexEntrySize);
							IndexEntryKey key;
							long index;
							if (_version == PTableVersions.IndexV4) {
								key = new IndexEntryKey(BitConverter.ToUInt64(buffer, 8),
									BitConverter.ToInt64(buffer, 0));
								index = BitConverter.ToInt64(buffer, 8 + 8);
							} else
								throw new InvalidOperationException("Unknown PTable version: " + _version);

							midpoints.Add(new Midpoint(key, index));

							if (k > 0) {
								if (midpoints[k].Key.GreaterThan(midpoints[k - 1].Key)) {
									throw new CorruptIndexException(String.Format(
										"Index entry key for midpoint {0} (stream: {1}, version: {2}) < index entry key for midpoint {3} (stream: {4}, version: {5})",
										k - 1, midpoints[k - 1].Key.Stream, midpoints[k - 1].Key.Version, k,
										midpoints[k].Key.Stream, midpoints[k].Key.Version));
								} else if (midpoints[k - 1].ItemIndex > midpoints[k].ItemIndex) {
									throw new CorruptIndexException(String.Format(
										"Item index for midpoint {0} ({1}) > Item index for midpoint {2} ({3})",
										k - 1, midpoints[k - 1].ItemIndex, k, midpoints[k].ItemIndex));
								}
							}
						}

						return midpoints;
					} else
						Log.Debug(
							"Skipping loading of cached midpoints from PTable due to count mismatch, cached midpoints: {midpointsCached} / required midpoints: {midpointsCount}",
							_midpointsCached, midpointsCount);
				}

				if (!skipIndexVerify) {
					stream.Seek(0, SeekOrigin.Begin);
					stream.Read(buffer, 0, PTableHeader.Size);
					md5.TransformBlock(buffer, 0, PTableHeader.Size, null, 0);
				}

				long previousNextIndex = long.MinValue;
				var previousKey = new IndexEntryKey(long.MaxValue, long.MaxValue);
				for (int k = 0; k < midpointsCount; ++k) {
					long nextIndex = GetMidpointIndex(k, count, midpointsCount);
					if (previousNextIndex != nextIndex) {
						if (!skipIndexVerify) {
							ReadUntilWithMd5(PTableHeader.Size + _indexEntrySize * nextIndex, stream, md5);
							stream.Read(buffer, 0, _indexKeySize);
							md5.TransformBlock(buffer, 0, _indexKeySize, null, 0);
						} else {
							stream.Seek(PTableHeader.Size + _indexEntrySize * nextIndex, SeekOrigin.Begin);
							stream.Read(buffer, 0, _indexKeySize);
						}

						IndexEntryKey key;
						if (_version == PTableVersions.IndexV1) {
							key = new IndexEntryKey(BitConverter.ToUInt32(buffer, 4),
								BitConverter.ToInt32(buffer, 0));
						} else if (_version == PTableVersions.IndexV2) {
							key = new IndexEntryKey(BitConverter.ToUInt64(buffer, 4),
								BitConverter.ToInt32(buffer, 0));
						} else {
							key = new IndexEntryKey(BitConverter.ToUInt64(buffer, 8),
								BitConverter.ToInt64(buffer, 0));
						}

						midpoints.Add(new Midpoint(key, nextIndex));
						previousNextIndex = nextIndex;
						previousKey = key;
					} else {
						midpoints.Add(new Midpoint(previousKey, previousNextIndex));
					}

					if (k > 0) {
						if (midpoints[k].Key.GreaterThan(midpoints[k - 1].Key)) {
							throw new CorruptIndexException(String.Format(
								"Index entry key for midpoint {0} (stream: {1}, version: {2}) < index entry key for midpoint {3} (stream: {4}, version: {5})",
								k - 1, midpoints[k - 1].Key.Stream, midpoints[k - 1].Key.Version, k,
								midpoints[k].Key.Stream, midpoints[k].Key.Version));
						} else if (midpoints[k - 1].ItemIndex > midpoints[k].ItemIndex) {
							throw new CorruptIndexException(String.Format(
								"Item index for midpoint {0} ({1}) > Item index for midpoint {2} ({3})", k - 1,
								midpoints[k - 1].ItemIndex, k, midpoints[k].ItemIndex));
						}
					}
				}

				if (!skipIndexVerify) {
					ReadUntilWithMd5(stream.Length - MD5Size, stream, md5);
					//verify hash (should be at stream.length - MD5Size)
					md5.TransformFinalBlock(Empty.ByteArray, 0, 0);
					var fileHash = new byte[MD5Size];
					stream.Read(fileHash, 0, MD5Size);
					ValidateHash(md5.Hash, fileHash);
				}

				return midpoints;
			}
		} catch (PossibleToHandleOutOfMemoryException) {
			midpoints?.Dispose();
			throw;
		} catch {
			midpoints?.Dispose();
			Dispose();
			throw;
		} finally {
			if (RuntimeInformation.IsUnix) {
				if (workItem != null)
					ReturnWorkItem(workItem);
			} else {
				stream?.Dispose();
			}
		}
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

	private readonly byte[] TmpReadBuf = new byte[DefaultBufferSize];

	private void ReadUntilWithMd5(long nextPos, Stream fileStream, HashAlgorithm md5) {
		long toRead = nextPos - fileStream.Position;
		if (toRead < 0)
			throw new Exception("should not do negative reads.");
		while (toRead > 0) {
			var localReadCount = Math.Min(toRead, TmpReadBuf.Length);
			int read = fileStream.Read(TmpReadBuf, 0, (int)localReadCount);
			md5.TransformBlock(TmpReadBuf, 0, read, null, 0);
			toRead -= read;
		}
	}

	void ValidateHash(byte[] fromFile, byte[] computed) {
		if (computed == null)
			throw new CorruptIndexException(new HashValidationException("Calculated MD5 hash is null!"));
		if (fromFile == null)
			throw new CorruptIndexException(new HashValidationException("Read from file MD5 hash is null!"));

		if (computed.Length != fromFile.Length)
			throw new CorruptIndexException(
				new HashValidationException(
					string.Format(
						"Hash sizes differ! FileHash({0}): {1}, hash({2}): {3}.",
						computed.Length,
						BitConverter.ToString(computed),
						fromFile.Length,
						BitConverter.ToString(fromFile))));

		for (int i = 0; i < fromFile.Length; i++) {
			if (fromFile[i] != computed[i])
				throw new CorruptIndexException(
					new HashValidationException(
						string.Format(
							"Hashes are different! computed: {0}, hash: {1}.",
							BitConverter.ToString(computed),
							BitConverter.ToString(fromFile))));
		}
	}

	public IEnumerable<IndexEntry> IterateAllInOrder() {
		var workItem = GetWorkItem();
		try {
			workItem.Stream.Position = PTableHeader.Size;
			for (long i = 0, n = Count; i < n; i++) {
				yield return ReadNextNoSeek(workItem, _version);
			}
		} finally {
			ReturnWorkItem(workItem);
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

		entry = ReadEntry(_indexEntrySize, value.LatestOffset, _version);
		return true;
	}

	public async ValueTask<IndexEntry?> TryGetLatestEntry(
		ulong stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		CancellationToken token) {

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
				return await TryGetLatestEntryFast(
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
				return await TryGetLatestEntrySlow(
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
	private async ValueTask<IndexEntry?> TryGetLatestEntrySlow(
		StreamHash stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		Range recordRange,
		IndexEntryKey lowBoundsCheck,
		IndexEntryKey highBoundsCheck,
		WorkItem workItem,
		CancellationToken token) {

		long maxBeforePosition = long.MinValue;
		IndexEntry maxEntry = default;

		for (var idx = recordRange.Lower; idx <= recordRange.Upper; idx++) {
			var candidateEntry = ReadEntry(_indexEntrySize, idx, workItem, _version);
			var candidateEntryKey = new IndexEntryKey(candidateEntry.Stream, candidateEntry.Version);

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

	private async ValueTask<IndexEntry?> TryGetLatestEntryFast(
		StreamHash stream,
		long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream,
		Range recordRange,
		IndexEntryKey lowBoundsCheck,
		IndexEntryKey highBoundsCheck,
		WorkItem workItem,
		CancellationToken token) {

		var startKey = BuildKey(stream, 0);
		var endKey = BuildKey(stream, long.MaxValue);

		var low = recordRange.Lower;
		var high = recordRange.Upper;

		while (low < high) {
			var mid = low + (high - low) / 2;
			IndexEntry midpoint = ReadEntry(_indexEntrySize, mid, workItem, _version);

			var midpointKey = new IndexEntryKey(midpoint.Stream, midpoint.Version);
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
				} else if (midpointKey.SmallerThan(startKey)){
					high = mid - 1;
					highBoundsCheck = midpointKey;
				} else 	throw new MaybeCorruptIndexException(
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

		var candidateEntry = ReadEntry(_indexEntrySize, high, workItem, _version);

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
			var candEntry = ReadEntry(_indexEntrySize, high, workItem, _version);
			var candKey = new IndexEntryKey(candEntry.Stream, candEntry.Version);

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

		entry = ReadEntry(_indexEntrySize, value.OldestOffset, _version);
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
			var candEntry = ReadEntry(_indexEntrySize, high, workItem, _version);
			var candidateKey = new IndexEntryKey(candEntry.Stream, candEntry.Version);
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

		ReadOnlySpan<Midpoint> midpoints = null;
		if (_midpoints != null) {
			midpoints = _midpoints.AsSpan();
		}

		if (midpoints == null)
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

	private static void PositionAtEntry(int indexEntrySize, long indexNum, WorkItem workItem) {
		workItem.Stream.Seek(indexEntrySize * indexNum + PTableHeader.Size, SeekOrigin.Begin);
	}

	private static IndexEntry ReadEntry(int indexEntrySize, long indexNum, WorkItem workItem, int ptableVersion) {
		long seekTo = indexEntrySize * indexNum + PTableHeader.Size;
		workItem.Stream.Seek(seekTo, SeekOrigin.Begin);
		return ReadNextNoSeek(workItem, ptableVersion);
	}

	private IndexEntry ReadEntry(int indexEntrySize, long indexNum, int ptableVersion) {
		var workItem = GetWorkItem();
		try {
			return ReadEntry(_indexEntrySize, indexNum, workItem, ptableVersion);
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	private static IndexEntry ReadNextNoSeek(WorkItem workItem, int ptableVersion) {
		long version = (ptableVersion >= PTableVersions.IndexV3)
			? workItem.Reader.ReadInt64()
			: workItem.Reader.ReadInt32();
		ulong stream = ptableVersion == PTableVersions.IndexV1
			? workItem.Reader.ReadUInt32()
			: workItem.Reader.ReadUInt64();
		long position = workItem.Reader.ReadInt64();
		return new IndexEntry(stream, version, position);
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
			PositionAtEntry(_indexEntrySize, high, workItem);
			result = ReadForward(workItem, high, startKey, endKey, limit);
			return result;
		} catch (MaybeCorruptIndexException ex) {
			throw new MaybeCorruptIndexException(
				$"{ex.Message}. stream {stream}, startNum {startNumber}, endNum {endNumber}, PTable: {Filename}.");
		} finally {
			ReturnWorkItem(workItem);
		}
	}

	// forward here meaning forward in the file. towards the older records.
	private List<IndexEntry> ReadForward(WorkItem workItem, long high, IndexEntryKey startKey, IndexEntryKey endKey, int? limit) {

		var result = new List<IndexEntry>();

		for (long i = high, n = Count; i < n; ++i) {
			var entry = ReadNextNoSeek(workItem, _version);

			var candidateKey = new IndexEntryKey(entry.Stream, entry.Version);

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

	private long ChopForLatest(WorkItem workItem, IndexEntryKey endKey) {
		var recordRange = LocateRecordRange(endKey, out var lowBoundsCheck, out var highBoundsCheck);
		long low = recordRange.Lower;
		long high = recordRange.Upper;
		while (low < high) {
			var mid = low + (high - low) / 2;
			var midpoint = ReadEntry(_indexEntrySize, mid, workItem, _version);
			var midpointKey = new IndexEntryKey(midpoint.Stream, midpoint.Version);

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
		var recordRange = LocateRecordRange(startKey, out var lowBoundsCheck, out var highBoundsCheck);
		long low = recordRange.Lower;
		long high = recordRange.Upper;
		while (low < high) {
			var mid = low + (high - low + 1) / 2;
			var midpoint = ReadEntry(_indexEntrySize, mid, workItem, _version);
			var midpointKey = new IndexEntryKey(midpoint.Stream, midpoint.Version);

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

	public struct CacheEntry {
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

	public struct Midpoint {
		public readonly IndexEntryKey Key;
		public readonly long ItemIndex;

		public Midpoint(IndexEntryKey key, long itemIndex) {
			Key = key;
			ItemIndex = itemIndex;
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
			return string.Format("Stream: {0}, Version: {1}", Stream, Version);
		}
	}

	private class WorkItem : IDisposable {
		public readonly FileStream Stream;
		public readonly BinaryReader Reader;

		public WorkItem(string filename, int bufferSize) {
			Stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
				FileOptions.RandomAccess);
			Reader = new BinaryReader(Stream);
		}

		~WorkItem() {
			Dispose(false);
		}

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing) {
			if (disposing) {
				Stream.Dispose();
				Reader.Dispose();
			}
		}
	}
}

internal class HashCollisionException : Exception {
}
