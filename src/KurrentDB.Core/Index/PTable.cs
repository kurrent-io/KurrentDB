// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Buffers;
using DotNext.Buffers.Text;
using DotNext.Diagnostics;
using DotNext.IO;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.DataStructures.ProbabilisticFilter;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.TransactionLog.Unbuffered;
using Microsoft.Win32.SafeHandles;
using ILogger = Serilog.ILogger;
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
	public const int IndexEntryV1Size = sizeof(int) + sizeof(int) + sizeof(long);
	public const int IndexEntryV2Size = sizeof(int) + sizeof(long) + sizeof(long);
	public const int IndexEntryV3Size = sizeof(long) + sizeof(long) + sizeof(long);
	public const int IndexEntryV4Size = IndexEntryV3Size;

	public const int IndexKeyV1Size = sizeof(int) + sizeof(int);
	public const int IndexKeyV2Size = sizeof(int) + sizeof(long);
	public const int IndexKeyV3Size = sizeof(long) + sizeof(long);
	public const int IndexKeyV4Size = IndexKeyV3Size;
	public const int MD5Size = 16;
	private const int DefaultBufferSize = 8192;
	private const int DefaultSequentialBufferSize = 65536;
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

	// Handle lifetime is managed by AcquireFileHandle() and ReleaseFileHandle() methods
	// and built-in reference counting mechanism provided by SafeHandle class
	private readonly SafeFileHandle _handle;
	private readonly byte _version;
	private readonly int _indexEntrySize;
	private readonly int _indexKeySize;

	private readonly ManualResetEventSlim _destroyEvent = new(initialState: false);
	private volatile bool _deleteFile;
	private bool _disposed;
	private Atomic.Boolean _cleanupBarrier;

	public ReadOnlySpan<Midpoint> GetMidPoints()
		=> _midpoints is null ? [] : _midpoints.AsSpan();

	private PTable(string filename,
		Guid id,
		int depth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		ArgumentException.ThrowIfNullOrWhiteSpace(filename);
		ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
		ArgumentOutOfRangeException.ThrowIfNegative(depth);

		if (!File.Exists(filename))
			throw new CorruptIndexException(new PTableNotFoundException(filename));

		_id = id;
		_filename = filename;

		Log.Debug("Loading " + (skipIndexVerify ? "" : "and Verification ") + "of PTable '{pTable}' started...",
			Path.GetFileName(Filename));
		var sw = new Timestamp();
		_handle = File.OpenHandle(filename, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
		_size = RandomAccess.GetLength(_handle);

		Helper.EatException(_handle, static handle => {
			// this action will fail if the file is created on a Unix system that does not have permissions to make files read-only
			File.SetAttributes(handle, FileAttributes.ReadOnly | FileAttributes.NotContentIndexed);
		});

		try {
			var header = PTableHeader.Parse(_handle, fileOffset: 0L);
			long indexEntriesTotalSize = _size - PTableHeader.Size - MD5Size;
			switch (_version = header.Version) {
				case PTableVersions.IndexV1:
					throw new CorruptIndexException(new UnsupportedFileVersionException(
						_filename, header.Version, Version,
						"Detected a V1 index file, which is no longer supported. " +
						"The index will be backed up and rebuilt in a supported format. " +
						"This may take a long time for large databases. " +
						"You can also use a version of ESDB (>= 3.9.0 and < 24.10.0) to upgrade the " +
						"indexes to a supported format by performing an index merge."));
				case PTableVersions.IndexV2:
					_indexEntrySize = IndexEntryV2Size;
					_indexKeySize = IndexKeyV2Size;
					break;
				case PTableVersions.IndexV3:
					_indexEntrySize = IndexEntryV3Size;
					_indexKeySize = IndexKeyV3Size;
					break;
				case >= PTableVersions.IndexV4:
					//read the PTable footer
					var footer = PTableFooter.Parse(_handle, _size - MD5Size - PTableFooter.Size);
					if (footer.Version != header.Version)
						throw new CorruptIndexException(
							$"PTable header/footer version mismatch: {header.Version}/{footer.Version}",
							new InvalidFileException("Invalid PTable file."));

					_indexEntrySize = IndexEntryV4Size;
					_indexKeySize = IndexKeyV4Size;

					_midpointsCached = footer.NumMidpointsCached;
					_midpointsCacheSize = _midpointsCached * _indexEntrySize;
					indexEntriesTotalSize = indexEntriesTotalSize - PTableFooter.Size - _midpointsCacheSize;
					break;
				default:
					throw new CorruptIndexException(
						new UnsupportedFileVersionException(_filename, header.Version, Version));
			}

			if (indexEntriesTotalSize < 0) {
				throw new CorruptIndexException(
					$"Total size of index entries < 0: {indexEntriesTotalSize}. _size: {_size}, header size: {PTableHeader.Size}, _midpointsCacheSize: {_midpointsCacheSize}, version: {_version}, md5 size: {MD5Size}");
			} else if (indexEntriesTotalSize % _indexEntrySize is not 0) {
				throw new CorruptIndexException(
					$"Total size of index entries: {indexEntriesTotalSize} is not divisible by index entry size: {_indexEntrySize}");
			}

			_count = indexEntriesTotalSize / _indexEntrySize;

			if (_version >= PTableVersions.IndexV4 && _count > 0 && _midpointsCached is > 0 and < 2) {
				//if there is at least 1 index entry with version>=4 and there are cached midpoints, there should always be at least 2 midpoints cached
				throw new CorruptIndexException(
					$"Less than 2 midpoints cached in PTable. Index entries: {_count}, Midpoints cached: {_midpointsCached}");
			} else if (_count >= 2 && _midpointsCached > _count) {
				//if there are at least 2 index entries, midpoints count should be at most the number of index entries
				throw new CorruptIndexException(
					$"More midpoints cached in PTable than index entries. Midpoints: {_midpointsCached} , Index entries: {_count}");
			}

			if (Count is 0) {
				_minEntry = new IndexEntryKey(ulong.MaxValue, long.MaxValue);
				_maxEntry = new IndexEntryKey(ulong.MinValue, long.MinValue);
			} else {
				var minEntry = ReadEntry(_handle, _indexEntrySize, Count - 1, _version);
				_minEntry = new IndexEntryKey(minEntry.Stream, minEntry.Version);
				var maxEntry = ReadEntry(_handle, _indexEntrySize, 0, _version);
				_maxEntry = new IndexEntryKey(maxEntry.Stream, maxEntry.Version);
			}
		} catch (Exception) {
			Dispose();
			throw;
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

	private UnmanagedMemoryAppendOnlyList<Midpoint> CacheMidpointsAndVerifyHash(int depth, bool skipIndexVerify) {
		if (depth is < 0 or > 30)
			throw new ArgumentOutOfRangeException(nameof(depth));

		var count = Count;
		if (count is 0 || depth is 0)
			return null;

		if (skipIndexVerify) {
			Log.Debug("Disabling Verification of PTable");
		}

		var stream = OperatingSystem.IsWindows()
			? UnbufferedFileStream.Create(_filename, FileMode.Open, FileAccess.Read, FileShare.Read,
				4096, 4096, false, 4096)
			: _handle.AsUnbufferedStream(FileAccess.Read);

		UnmanagedMemoryAppendOnlyList<Midpoint> midpoints = null;

		var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		Span<byte> buffer = stackalloc byte[PTableHeader.Size];
		Debug.Assert(buffer.Length >= _indexEntrySize);
		Debug.Assert(buffer.Length >= _indexKeySize);

		var tmpBuffer = new SpanOwner<byte>(DefaultBufferSize, exactSize: false);
		try {
			int midpointsCount;
			try {
				midpointsCount = (int)Math.Max(2L, Math.Min((long)1 << depth, count));
				midpoints = new UnmanagedMemoryAppendOnlyList<Midpoint>(midpointsCount);
			} catch (OutOfMemoryException exc) {
				throw new PossibleToHandleOutOfMemoryException("Failed to allocate memory for Midpoint cache.",
					exc);
			}

			switch (skipIndexVerify) {
				case true when _version >= PTableVersions.IndexV4:
					if (_midpointsCached == midpointsCount) {
						//index verification is disabled and cached midpoints with the same depth requested are available
						//so, we can load them directly from the PTable file
						Log.Debug("Loading {midpointsCached} cached midpoints from PTable", _midpointsCached);
						long startOffset = stream.Length - MD5Size - PTableFooter.Size -
						                   _midpointsCacheSize;
						stream.Seek(startOffset, SeekOrigin.Begin);
						for (int k = 0; k < (int)_midpointsCached; k++) {
							stream.ReadExactly(buffer.Slice(0, _indexEntrySize));
							var key = new IndexEntryKey(BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(sizeof(long))),
								BinaryPrimitives.ReadInt64LittleEndian(buffer));
							var index = BinaryPrimitives.ReadInt64LittleEndian(
								buffer.Slice(sizeof(long) + sizeof(long)));

							midpoints.Add(new Midpoint(key, index));

							if (k > 0) {
								if (midpoints[k].Key.GreaterThan(midpoints[k - 1].Key)) {
									throw new CorruptIndexException(
										$"Index entry key for midpoint {k - 1} (stream: {midpoints[k - 1].Key.Stream}, version: {midpoints[k - 1].Key.Version}) < index entry key for midpoint {k} (stream: {midpoints[k].Key.Stream}, version: {midpoints[k].Key.Version})");
								} else if (midpoints[k - 1].ItemIndex > midpoints[k].ItemIndex) {
									throw new CorruptIndexException(
										$"Item index for midpoint {k - 1} ({midpoints[k - 1].ItemIndex}) > Item index for midpoint {k} ({midpoints[k].ItemIndex})");
								}
							}
						}

						return midpoints;
					}

					Log.Debug(
						"Skipping loading of cached midpoints from PTable due to count mismatch, cached midpoints: {midpointsCached} / required midpoints: {midpointsCount}",
						_midpointsCached, midpointsCount);
					break;
				case false:
					stream.Seek(0, SeekOrigin.Begin);
					stream.ReadExactly(buffer.Slice(0, PTableHeader.Size));
					md5.AppendData(buffer.Slice(0, PTableHeader.Size));
					break;
			}

			long previousNextIndex = long.MinValue;
			var previousKey = new IndexEntryKey(long.MaxValue, long.MaxValue);
			for (int k = 0; k < midpointsCount; ++k) {
				long nextIndex = GetMidpointIndex(k, count, midpointsCount);
				if (previousNextIndex != nextIndex) {
					if (!skipIndexVerify) {
						ReadUntilWithMd5(PTableHeader.Size + _indexEntrySize * nextIndex, stream, md5, tmpBuffer.Span);
						stream.ReadExactly(buffer.Slice(0, _indexKeySize));
						md5.AppendData(buffer.Slice(0, _indexKeySize));
					} else {
						stream.Seek(PTableHeader.Size + _indexEntrySize * nextIndex, SeekOrigin.Begin);
						stream.ReadExactly(buffer.Slice(0, _indexKeySize));
					}

					IndexEntryKey key = _version switch {
						PTableVersions.IndexV1 => new IndexEntryKey(
							BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(sizeof(int))),
							BinaryPrimitives.ReadInt32LittleEndian(buffer)),
						PTableVersions.IndexV2 => new IndexEntryKey(
							BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(sizeof(int))),
							BinaryPrimitives.ReadInt32LittleEndian(buffer)),
						_ => new IndexEntryKey(BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(sizeof(long))),
							BinaryPrimitives.ReadInt64LittleEndian(buffer))
					};

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
				ValidateHash(stream, md5, tmpBuffer.Span);
			}

			return midpoints;
		} catch (PossibleToHandleOutOfMemoryException) {
			midpoints?.Dispose();
			throw;
		} catch {
			midpoints?.Dispose();
			Dispose();
			throw;
		} finally {
			md5.Dispose();
			stream.Dispose();
			tmpBuffer.Dispose();
		}
	}

	private void AcquireFileHandle() {
		var acquired = false;
		try {
			_handle.DangerousAddRef(ref acquired);
		} catch (ObjectDisposedException) {
			if (_cleanupBarrier.FalseToTrue()) {
				DeleteFileIfNeeded();
			}

			throw new FileBeingDeletedException();
		}
	}

	private void ReleaseFileHandle() {
		_handle.DangerousRelease();

		if (_handle.IsClosed && _cleanupBarrier.FalseToTrue()) {
			DeleteFileIfNeeded();
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

	private static void ReadUntilWithMd5(long nextPos, Stream fileStream, IncrementalHash md5, Span<byte> tmpBuffer) {
		long toRead = nextPos - fileStream.Position;
		if (toRead < 0)
			throw new Exception("should not do negative reads.");
		while (toRead > 0) {
			int read = fileStream.Read(tmpBuffer.TrimLength(int.CreateSaturating(toRead)));
			md5.AppendData(tmpBuffer.Slice(0, read));
			toRead -= read;
		}
	}

	private static void ValidateHash(Stream stream, IncrementalHash actual, Span<byte> buffer) {
		ReadUntilWithMd5(stream.Length - MD5Size, stream, actual, buffer);
		//verify hash (should be at stream.length - MD5Size)
		Span<byte> fileHash = stackalloc byte[MD5Size];
		stream.ReadExactly(fileHash);
		ValidateHash(fileHash, actual);
	}

	private static void ValidateHash(Span<byte> expected, IncrementalHash actual) {
		Debug.Assert(actual is not null);

		Span<byte> actualHash = stackalloc byte[MD5Size];
		var bytesWritten = actual.GetCurrentHash(actualHash);
		Debug.Assert(bytesWritten is MD5Size);

		// Perf: use hardware accelerated byte array comparison
		if (!expected.SequenceEqual(actualHash)) {
			throw new CorruptIndexException(
				new HashValidationException(
					$"Hashes are different! computed: {Hex.EncodeToUtf16(actualHash)}, hash: {Hex.EncodeToUtf16(expected)}."));
		}
	}

	public IEnumerable<IndexEntry> IterateAllInOrder() {
		AcquireFileHandle();
		try {
			long fileOffset = PTableHeader.Size;
			for (long i = 0, n = Count; i < n; i++) {
				yield return ReadEntry(_handle, fileOffset, _version, out var bytesRead);
				fileOffset += bytesRead;
			}
		} finally {
			ReleaseFileHandle();
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

		AcquireFileHandle();
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
					token);
			}
		} finally {
			ReleaseFileHandle();
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
		CancellationToken token) {

		long maxBeforePosition = long.MinValue;
		IndexEntry maxEntry = default;

		for (var idx = recordRange.Lower; idx <= recordRange.Upper; idx++) {
			var candidateEntry = ReadEntry(idx);
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
		CancellationToken token) {

		var startKey = BuildKey(stream, 0);
		var endKey = BuildKey(stream, long.MaxValue);

		var low = recordRange.Lower;
		var high = recordRange.Upper;

		while (low < high) {
			var mid = low + (high - low) / 2;
			IndexEntry midpoint = ReadEntry(mid);

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

		var candidateEntry = ReadEntry(high);

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

		AcquireFileHandle();
		try {
			var high = ChopForLatest(endKey);
			var candEntry = ReadEntry(high);
			var candKey = new IndexEntryKey(candEntry.Stream, candEntry.Version);

			if (candKey.GreaterThan(endKey))
				throw new MaybeCorruptIndexException(
					$"candEntry ({candEntry.Stream}@{candEntry.Version}) > startKey {startKey}, stream {stream}, startNum {startNumber}, endNum {endNumber}, PTable: {Filename}.");
			if (candKey.SmallerThan(startKey)) {
				offset = default;
				return false;
			}

			entry = candEntry;
			offset = high;
			return true;
		} finally {
			ReleaseFileHandle();
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

		AcquireFileHandle();
		try {
			var high = ChopForOldest(startKey);
			var candEntry = ReadEntry(high);
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
			ReleaseFileHandle();
		}
	}

	public IReadOnlyList<IndexEntry> GetRange(ulong stream, long startNumber, long endNumber, int? limit = null) {
		ArgumentOutOfRangeException.ThrowIfNegative(startNumber);
		ArgumentOutOfRangeException.ThrowIfNegative(endNumber);

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

	private static long PositionAtEntry(int indexEntrySize, long indexNum)
		=> indexEntrySize * indexNum + PTableHeader.Size;

	private static IndexEntry ReadEntry(SafeFileHandle handle, int indexEntrySize, long indexNum, byte ptableVersion)
		=> ReadEntry(handle, PositionAtEntry(indexEntrySize, indexNum), ptableVersion, out _);

	private IndexEntry ReadEntry(long indexNum) {
		AcquireFileHandle();
		try {
			return ReadEntry(_handle, _indexEntrySize, indexNum, _version);
		} finally {
			ReleaseFileHandle();
		}
	}

	private static IndexEntry ReadEntry(SafeFileHandle handle, long fileOffset, byte ptableVersion, out int bytesRead) {
		// allocate buffer for the worst case
		Span<byte> buffer = stackalloc byte[IndexEntryV4Size];
		var reader = new SpanReader<byte>(buffer);
		long version;
		ulong stream;
		switch (ptableVersion) {
			case PTableVersions.IndexV1:
				buffer = buffer.Slice(0, IndexEntryV1Size);
				RandomAccess.Read(handle, buffer, fileOffset);
				version = reader.ReadLittleEndian<int>();
				stream = reader.ReadLittleEndian<uint>();
				break;
			case PTableVersions.IndexV2:
				buffer = buffer.Slice(0, IndexEntryV2Size);
				RandomAccess.Read(handle, buffer, fileOffset);
				version = reader.ReadLittleEndian<int>();
				stream = reader.ReadLittleEndian<ulong>();
				break;
			default:
				// V3 or higher
				RandomAccess.Read(handle, buffer, fileOffset);
				version = reader.ReadLittleEndian<long>();
				stream = reader.ReadLittleEndian<ulong>();
				break;
		}

		var position = reader.ReadLittleEndian<long>();
		bytesRead = reader.ConsumedCount;

		Debug.Assert(bytesRead == GetIndexEntrySize(ptableVersion));
		return new(stream, version, position);
	}

	private void DisposeFileHandle() {
		_handle.Dispose(); // it decrements the counter

		if (_handle.IsClosed && _cleanupBarrier.FalseToTrue()) {
			DeleteFileIfNeeded();
		}
	}

	public void MarkForDestruction() {
		_deleteFile = true;
		DisposeFileHandle();
	}

	public void Dispose() {
		_deleteFile = false;
		DisposeFileHandle();
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

	private void DeleteFileIfNeeded() {
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

		AcquireFileHandle();
		try {
			var high = tableLatestOffset ?? ChopForLatest(endKey);
			result = ReadForward(PositionAtEntry(_indexEntrySize, high), high, startKey, endKey, limit);
			return result;
		} catch (MaybeCorruptIndexException ex) {
			throw new MaybeCorruptIndexException(
				$"{ex.Message}. stream {stream}, startNum {startNumber}, endNum {endNumber}, PTable: {Filename}.");
		} finally {
			ReleaseFileHandle();
		}
	}

	// forward here meaning forward in the file. towards the older records.
	private List<IndexEntry> ReadForward(long position, long high, IndexEntryKey startKey, IndexEntryKey endKey, int? limit) {

		var result = new List<IndexEntry>();

		for (long i = high, n = Count; i < n; ++i) {
			var entry = ReadEntry(_handle, position, _version, out var bytesRead);
			position += bytesRead;

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

	private long ChopForLatest(IndexEntryKey endKey) {
		var recordRange = LocateRecordRange(endKey, out var lowBoundsCheck, out var highBoundsCheck);
		long low = recordRange.Lower;
		long high = recordRange.Upper;
		while (low < high) {
			var mid = low + (high - low) / 2;
			var midpoint = ReadEntry(mid);
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

	private long ChopForOldest(IndexEntryKey startKey) {
		var recordRange = LocateRecordRange(startKey, out var lowBoundsCheck, out var highBoundsCheck);
		long low = recordRange.Lower;
		long high = recordRange.Upper;
		while (low < high) {
			var mid = low + (high - low + 1) / 2;
			var midpoint = ReadEntry(mid);
			var midpointKey = new IndexEntryKey(midpoint.Stream, midpoint.Version);

			if (midpointKey.GreaterThan(lowBoundsCheck)) {
				throw new MaybeCorruptIndexException(
					$"Midpoint key (stream: {midpointKey.Stream}, version: {midpointKey.Version}) > low bounds check key (stream: {lowBoundsCheck.Stream}, version: {lowBoundsCheck.Version})");
			} else if (!midpointKey.GreaterEqualsThan(highBoundsCheck)) {
				throw new MaybeCorruptIndexException(
					$"Midpoint key (stream: {midpointKey.Stream}, version: {midpointKey.Version}) < high bounds check key (stream: {highBoundsCheck.Stream}, version: {highBoundsCheck.Version})");
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
		AcquireFileHandle();
		try {
			return _bloomFilter.MightContain(Span.AsReadOnlyBytes(in stream.Hash));
		} finally {
			ReleaseFileHandle();
		}
	}

	// construct this struct with a 64 bit hash and it will convert it to a hash
	// for the specified table version
	public readonly struct StreamHash : IEquatable<StreamHash> {
		public readonly ulong Hash;

		public StreamHash(byte version, ulong hash) {
			Hash = version is PTableVersions.IndexV1 ? hash >> 32 : hash;
		}

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
}

internal class HashCollisionException : Exception {
}
