// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.DataStructures;
using EventStore.Core.DataStructures.ProbabilisticFilter;
using MD5 = EventStore.Core.Hashing.MD5;

namespace EventStore.Core.Index;

public partial class PTable {
	public static PTable FromFile(string filename, int initialReaders, int maxReaders,
		int cacheDepth, bool skipIndexVerify,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		return new PTable(filename, Guid.NewGuid(), initialReaders, maxReaders,
			cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize);
	}

	private const int MidpointsOverflowSafetyNet = 20;

	public static PersistentBloomFilter ConstructBloomFilter(
		bool useBloomFilter,
		string filename,
		long indexEntryCount,
		Func<long, long> genBloomFilterSizeBytes = null) {

		if (!useBloomFilter)
			return null;

		try {
			genBloomFilterSizeBytes ??= GenBloomFilterSizeBytes;
			return new PersistentBloomFilter(
				new FileStreamPersistence(
					path: GenBloomFilterFilename(filename),
					create: true,
					size: genBloomFilterSizeBytes(indexEntryCount)));
		} catch (OutOfMemoryException ex) {
			Log.Warning(ex, "Could not allocate enough memory for Bloom filter for index file {file}. Performance will be degraded", filename);
			return null;
		} catch (Exception ex) {
			Log.Error(ex, "Could not create Bloom filter for index file {file}. Performance will be degraded", filename);
			return null;
		}
	}

	public static PTable FromMemtable(IMemTable table, string filename, int initialReaders, int maxReaders,
		int cacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		Ensure.NotNull(table, "table");
		Ensure.NotNullOrEmpty(filename, "filename");
		Ensure.Nonnegative(cacheDepth, "cacheDepth");

		int indexEntrySize = GetIndexEntrySize(table.Version);
		long dumpedEntryCount = 0;

		var sw = Stopwatch.StartNew();
		using (var fs = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
			DefaultSequentialBufferSize, FileOptions.SequentialScan)) {

			var fileSize = GetFileSizeUpToIndexEntries(table.Count, table.Version);
			fs.SetLength(fileSize);
			fs.Seek(0, SeekOrigin.Begin);

			using (var bloomFilter = ConstructBloomFilter(useBloomFilter, filename, table.Count))
			using (var md5 = MD5.Create())
			using (var cs = new CryptoStream(fs, md5, CryptoStreamMode.Write))
			using (var bs = new BufferedStream(cs, DefaultSequentialBufferSize)) {
				// WRITE HEADER
				var headerBytes = new PTableHeader(table.Version).AsByteArray();
				cs.Write(headerBytes, 0, headerBytes.Length);

				// WRITE INDEX ENTRIES
				var buffer = new byte[indexEntrySize];
				var records = table.IterateAllInOrder();
				var requiredMidpointCount = GetRequiredMidpointCountCached(table.Count, table.Version, cacheDepth);
				using var midpoints = new UnmanagedMemoryAppendOnlyList<Midpoint>((int)requiredMidpointCount + MidpointsOverflowSafetyNet);

				var midpointCalculator = new MidpointIndexCalculator(table.Count, requiredMidpointCount);
				long indexEntry = 0L;

				ulong? previousHash = null;
				foreach (var rec in records) {
					AppendRecordTo(bs, buffer, table.Version, rec, indexEntrySize);
					dumpedEntryCount += 1;
					if (table.Version >= PTableVersions.IndexV4 &&
						indexEntry == midpointCalculator.NextMidpointIndex) {
						midpoints.Add(new Midpoint(new IndexEntryKey(rec.Stream, rec.Version), indexEntry));
						midpointCalculator.Advance();
					}

					// WRITE BLOOM FILTER ENTRY
					if (bloomFilter != null && rec.Stream != previousHash) {
						// we are creating a PTable of the same version as the Memtable. therefore the hash is the right format
						var streamHash = rec.Stream;
						bloomFilter.Add(GetSpan(ref streamHash));
						previousHash = rec.Stream;
					}

					indexEntry++;
				}

				//WRITE MIDPOINTS
				if (table.Version >= PTableVersions.IndexV4) {
					var numIndexEntries = table.Count;
					if (dumpedEntryCount != numIndexEntries) {
						//if index entries have been removed, compute the midpoints again
						numIndexEntries = dumpedEntryCount;
						requiredMidpointCount =
							GetRequiredMidpointCount(numIndexEntries, table.Version, cacheDepth);
						ComputeMidpoints(bs, fs, table.Version, indexEntrySize, numIndexEntries,
							requiredMidpointCount, midpoints);
					}

					if (midpoints.Count > 0) {
						if (midpoints[0].ItemIndex != 0)
							throw new Exception("First midpoint was not the first index entry");
						if (midpoints[^1].ItemIndex != numIndexEntries - 1)
							throw new Exception("Last midpoint was not the last index entry");
					}

					WriteMidpointsTo(bs, fs, table.Version, indexEntrySize, buffer, dumpedEntryCount,
						numIndexEntries, requiredMidpointCount, midpoints);
				}

				bloomFilter?.Flush();
				bs.Flush();
				cs.FlushFinalBlock();

				// WRITE MD5
				var hash = md5.Hash;
				fs.SetLength(fs.Position + MD5Size);
				fs.Write(hash, 0, hash.Length);
				fs.FlushToDisk();
			}
		}

		Log.Debug("Dumped MemTable [{id}, {table} entries] in {elapsed}.", table.Id, table.Count, sw.Elapsed);
		return new PTable(filename, table.Id, initialReaders, maxReaders, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize);
	}

	public static PTable MergeTo(
		IList<PTable> tables,
		string outputFile,
		byte version,
		int initialReaders,
		int maxReaders,
		int cacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		Ensure.NotNull(tables, "tables");
		Ensure.NotNullOrEmpty(outputFile, "outputFile");
		Ensure.Nonnegative(cacheDepth, "cacheDepth");

		var indexEntrySize = GetIndexEntrySize(version);

		long numIndexEntries = 0;
		for (var i = 0; i < tables.Count; i++)
			numIndexEntries += tables[i].Count;

		var fileSizeUpToIndexEntries = GetFileSizeUpToIndexEntries(numIndexEntries, version);
		if (tables.Count == 2)
			return MergeTo2(tables, numIndexEntries, indexEntrySize, outputFile,
				version, initialReaders, maxReaders, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize); // special case

		Log.Debug("PTables merge started.");
		var watch = Stopwatch.StartNew();

		var enumerators = tables.Select(Get64bitEnumerator).ToList();
		try {
			for (int i = 0; i < enumerators.Count; i++) {
				if (!enumerators[i].MoveNext()) {
					enumerators[i].Dispose();
					enumerators.RemoveAt(i);
					i--;
				}
			}

			long dumpedEntryCount = 0;
			using (var f = new FileStream(outputFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
				DefaultSequentialBufferSize, FileOptions.SequentialScan)) {
				f.SetLength(fileSizeUpToIndexEntries);
				f.Seek(0, SeekOrigin.Begin);

				using (var bloomFilter = ConstructBloomFilter(useBloomFilter, outputFile, tables.Sum(table => table.Count)))
				using (var md5 = MD5.Create())
				using (var cs = new CryptoStream(f, md5, CryptoStreamMode.Write))
				using (var bs = new BufferedStream(cs, DefaultSequentialBufferSize)) {
					// WRITE HEADER
					var headerBytes = new PTableHeader(version).AsByteArray();
					cs.Write(headerBytes, 0, headerBytes.Length);

					var buffer = new byte[indexEntrySize];

					var requiredMidpointCount = GetRequiredMidpointCountCached(numIndexEntries, version, cacheDepth);
					using var midpoints = new UnmanagedMemoryAppendOnlyList<Midpoint>((int)requiredMidpointCount + MidpointsOverflowSafetyNet);

					var midpointCalculator = new MidpointIndexCalculator(numIndexEntries, requiredMidpointCount);
					long indexEntry = 0L;

					// WRITE INDEX ENTRIES
					ulong? previousHash = null;
					while (enumerators.Count > 0) {
						var idx = GetMaxOf(enumerators);
						var current = enumerators[idx].Current;
						AppendRecordTo(bs, buffer, version, current, indexEntrySize);
						if (version >= PTableVersions.IndexV4 &&
							indexEntry == midpointCalculator.NextMidpointIndex) {
							midpoints.Add(new Midpoint(new IndexEntryKey(current.Stream, current.Version), indexEntry));
							midpointCalculator.Advance();
						}

						// WRITE BLOOM FILTER ENTRY
						if (bloomFilter != null && current.Stream != previousHash) {
							var streamHash = current.Stream;
							bloomFilter.Add(GetSpan(ref streamHash));
							previousHash = current.Stream;
						}

						indexEntry++;
						dumpedEntryCount++;

						if (!enumerators[idx].MoveNext()) {
							enumerators[idx].Dispose();
							enumerators.RemoveAt(idx);
						}
					}

					//WRITE MIDPOINTS
					if (version >= PTableVersions.IndexV4) {
						if (dumpedEntryCount != numIndexEntries) {
							//if index entries have been removed, compute the midpoints again
							numIndexEntries = dumpedEntryCount;
							requiredMidpointCount = GetRequiredMidpointCount(numIndexEntries, version, cacheDepth);
							ComputeMidpoints(bs, f, version, indexEntrySize, numIndexEntries,
								requiredMidpointCount, midpoints);
						}

						if (midpoints.Count > 0) {
							if (midpoints[0].ItemIndex != 0)
								throw new Exception("First midpoint was not the first index entry");
							if (midpoints[^1].ItemIndex != numIndexEntries - 1)
								throw new Exception("Last midpoint was not the last index entry");
						}

						WriteMidpointsTo(bs, f, version, indexEntrySize, buffer, dumpedEntryCount, numIndexEntries,
							requiredMidpointCount, midpoints);
					}

					bloomFilter?.Flush();
					bs.Flush();
					cs.FlushFinalBlock();

					f.FlushToDisk();
					f.SetLength(f.Position + MD5Size);

					// WRITE MD5
					var hash = md5.Hash;
					f.Write(hash, 0, hash.Length);
					f.FlushToDisk();
				}
			}

			Log.Debug(
				"PTables merge finished in {elapsed} ([{entryCount}] entries merged into {dumpedEntryCount}).",
				watch.Elapsed, string.Join(", ", tables.Select(x => x.Count)), dumpedEntryCount);
			return new PTable(outputFile, Guid.NewGuid(), initialReaders, maxReaders, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize);
		} finally {
			foreach (var enumerableTable in enumerators) {
				enumerableTable.Dispose();
			}
		}
	}

	public static int GetIndexEntrySize(byte version) {
		if (version == PTableVersions.IndexV1) {
			return PTable.IndexEntryV1Size;
		}

		if (version == PTableVersions.IndexV2) {
			return PTable.IndexEntryV2Size;
		}

		if (version == PTableVersions.IndexV3) {
			return PTable.IndexEntryV3Size;
		}

		return PTable.IndexEntryV4Size;
	}

	private static PTable MergeTo2(IList<PTable> tables, long numIndexEntries, int indexEntrySize,
		string outputFile,
		byte version, int initialReaders, int maxReaders,
		int cacheDepth, bool skipIndexVerify,
		bool useBloomFilter, int lruCacheSize) {

		Log.Debug("PTables merge started (specialized for <= 2 tables).");
		var watch = Stopwatch.StartNew();

		var fileSizeUpToIndexEntries = GetFileSizeUpToIndexEntries(numIndexEntries, version);
		var enumerators = tables.Select(Get64bitEnumerator).ToList();
		try {
			long dumpedEntryCount = 0;
			using (var f = new FileStream(outputFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
				DefaultSequentialBufferSize, FileOptions.SequentialScan)) {
				f.SetLength(fileSizeUpToIndexEntries);
				f.Seek(0, SeekOrigin.Begin);

				using (var bloomFilter = ConstructBloomFilter(useBloomFilter, outputFile, tables.Sum(table => table.Count)))
				using (var md5 = MD5.Create())
				using (var cs = new CryptoStream(f, md5, CryptoStreamMode.Write))
				using (var bs = new BufferedStream(cs, DefaultSequentialBufferSize)) {
					// WRITE HEADER
					var headerBytes = new PTableHeader(version).AsByteArray();
					cs.Write(headerBytes, 0, headerBytes.Length);

					// WRITE INDEX ENTRIES
					var buffer = new byte[indexEntrySize];
					var requiredMidpointCount =
						GetRequiredMidpointCountCached(numIndexEntries, version, cacheDepth);
					using var midpoints = new UnmanagedMemoryAppendOnlyList<Midpoint>((int)requiredMidpointCount + MidpointsOverflowSafetyNet);

					var midpointCalculator = new MidpointIndexCalculator(numIndexEntries, requiredMidpointCount);
					long indexEntry = 0L;

					var enum1 = enumerators[0];
					var enum2 = enumerators[1];
					bool available1 = enum1.MoveNext();
					bool available2 = enum2.MoveNext();
					IndexEntry current;
					ulong? previousHash = null;
					while (available1 || available2) {
						var entry1 = new IndexEntry(enum1.Current.Stream, enum1.Current.Version,
							enum1.Current.Position);
						var entry2 = new IndexEntry(enum2.Current.Stream, enum2.Current.Version,
							enum2.Current.Position);

						if (available1 && (!available2 || entry1.CompareTo(entry2) > 0)) {
							current = entry1;
							available1 = enum1.MoveNext();
						} else {
							current = entry2;
							available2 = enum2.MoveNext();
						}

						AppendRecordTo(bs, buffer, version, current, indexEntrySize);
						if (version >= PTableVersions.IndexV4 &&
							indexEntry == midpointCalculator.NextMidpointIndex) {
							midpoints.Add(new Midpoint(new IndexEntryKey(current.Stream, current.Version),
								indexEntry));
							midpointCalculator.Advance();
						}

						// WRITE BLOOM FILTER ENTRY
						if (bloomFilter != null && current.Stream != previousHash) {
							// upgradeHash has already ensured the hash is in the right format for the target
							var streamHash = current.Stream;
							bloomFilter.Add(GetSpan(ref streamHash));
							previousHash = current.Stream;
						}

						indexEntry++;
						dumpedEntryCount++;
					}

					//WRITE MIDPOINTS
					if (version >= PTableVersions.IndexV4) {
						if (dumpedEntryCount != numIndexEntries) {
							//if index entries have been removed, compute the midpoints again
							numIndexEntries = dumpedEntryCount;
							requiredMidpointCount = GetRequiredMidpointCount(numIndexEntries, version, cacheDepth);
							ComputeMidpoints(bs, f, version, indexEntrySize, numIndexEntries,
								requiredMidpointCount, midpoints);
						}

						if (midpoints.Count > 0) {
							if (midpoints[0].ItemIndex != 0)
								throw new Exception("First midpoint was not the first index entry");
							if (midpoints[^1].ItemIndex != numIndexEntries - 1)
								throw new Exception("Last midpoint was not the last index entry");
						}

						WriteMidpointsTo(bs, f, version, indexEntrySize, buffer, dumpedEntryCount, numIndexEntries,
							requiredMidpointCount, midpoints);
					}

					bloomFilter?.Flush();
					bs.Flush();
					cs.FlushFinalBlock();

					f.SetLength(f.Position + MD5Size);

					// WRITE MD5
					var hash = md5.Hash;
					f.Write(hash, 0, hash.Length);
					f.FlushToDisk();
				}
			}

			Log.Debug(
				"PTables merge finished in {elapsed} ([{entryCount}] entries merged into {dumpedEntryCount}).",
				watch.Elapsed, string.Join(", ", tables.Select(x => x.Count)), dumpedEntryCount);
			return new PTable(outputFile, Guid.NewGuid(), initialReaders, maxReaders, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize);
		} finally {
			foreach (var enumerator in enumerators) {
				enumerator.Dispose();
			}
		}
	}

	public static async ValueTask<(PTable, long SpaceSaved)> Scavenged(
		PTable table,
		string outputFile,
		byte version,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		int initialReaders,
		int maxReaders,
		int cacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000,
		CancellationToken ct = default) {

		Ensure.NotNull(table, "table");
		Ensure.NotNullOrEmpty(outputFile, "outputFile");
		Ensure.Nonnegative(cacheDepth, "cacheDepth");

		var indexEntrySize = GetIndexEntrySize(version);
		var numIndexEntries = table.Count;

		var fileSizeUpToIndexEntries = GetFileSizeUpToIndexEntries(numIndexEntries, version);

		Log.Debug("PTables scavenge started with {numIndexEntries} entries.", numIndexEntries);
		var watch = Stopwatch.StartNew();
		long keptCount = 0L;
		long droppedCount;

		try {
			using (var f = new FileStream(outputFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
				DefaultSequentialBufferSize, FileOptions.SequentialScan)) {
				f.SetLength(fileSizeUpToIndexEntries);
				f.Seek(0, SeekOrigin.Begin);

				using (var bloomFilter = ConstructBloomFilter(useBloomFilter, outputFile, table.Count))
				using (var md5 = MD5.Create())
				using (var cs = new CryptoStream(f, md5, CryptoStreamMode.Write))
				using (var bs = new BufferedStream(cs, DefaultSequentialBufferSize)) {
					// WRITE HEADER
					var headerBytes = new PTableHeader(version).AsByteArray();
					cs.Write(headerBytes, 0, headerBytes.Length);

					// WRITE SCAVENGED INDEX ENTRIES
					var buffer = new byte[indexEntrySize];
					using (var enumerator = Get64bitEnumerator(table)) {

						ulong? previousHash = null;
						while (enumerator.MoveNext()) {
							if (await shouldKeep(enumerator.Current, ct)) {
								var current = enumerator.Current;
								AppendRecordTo(bs, buffer, version, enumerator.Current, indexEntrySize);
								// WRITE BLOOM FILTER ENTRY
								if (bloomFilter != null && current.Stream != previousHash) {
									var streamHash = current.Stream;
									bloomFilter.Add(GetSpan(ref streamHash));
									previousHash = current.Stream;
								}
								keptCount++;
							}
						}
					}

					droppedCount = numIndexEntries - keptCount;

					var forceKeep = version > table.Version;

					if (droppedCount == 0 && !forceKeep) {
						Log.Debug(
							"PTable scavenge finished in {elapsed}. No entries removed so not keeping scavenged table.",
							watch.Elapsed);

						try {
							bs.Close();
							File.Delete(outputFile);
						} catch (Exception ex) {
							Log.Error(ex, "Unable to delete unwanted scavenged PTable: {outputFile}",
								outputFile);
						}

						var bloomFilterFile = GenBloomFilterFilename(outputFile);
						try {
							File.Delete(bloomFilterFile);
						} catch (Exception ex) {
							Log.Error(ex, "Unable to delete unwanted bloom filter: {bloomFilterFile}",
								bloomFilterFile);
						}

						return (null, 0);
					}

					if (droppedCount == 0 && forceKeep) {
						Log.Debug("Keeping scavenged index even though it isn't smaller; version upgraded.");
					}

					//CALCULATE AND WRITE MIDPOINTS
					if (version >= PTableVersions.IndexV4) {
						var requiredMidpointCount = GetRequiredMidpointCount(keptCount, version, cacheDepth);
						using var midpoints =
							new UnmanagedMemoryAppendOnlyList<Midpoint>(
								(int)requiredMidpointCount + MidpointsOverflowSafetyNet);
						ComputeMidpoints(bs, f, version, indexEntrySize, keptCount,
							requiredMidpointCount, midpoints, ct);
						WriteMidpointsTo(bs, f, version, indexEntrySize, buffer, keptCount, keptCount,
							requiredMidpointCount, midpoints);
					}

					bloomFilter?.Flush();
					bs.Flush();
					cs.FlushFinalBlock();

					f.FlushToDisk();
					f.SetLength(f.Position + MD5Size);

					// WRITE MD5
					var hash = md5.Hash;
					f.Write(hash, 0, hash.Length);
					f.FlushToDisk();
				}
			}

			Log.Debug(
				"PTable scavenge finished in {elapsed} ({droppedCount} entries removed, {keptCount} remaining).",
				watch.Elapsed,
				droppedCount, keptCount);
			var scavengedTable = new PTable(outputFile, Guid.NewGuid(), initialReaders, maxReaders, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize);
			return (scavengedTable, table._size - scavengedTable._size);
		} catch (Exception) {
			try {
				File.Delete(outputFile);
			} catch (Exception ex) {
				Log.Error(ex, "Unable to delete unwanted scavenged PTable: {outputFile}", outputFile);
			}

			var bloomFilterFile = GenBloomFilterFilename(outputFile);
			try {
				File.Delete(bloomFilterFile);
			} catch (Exception ex) {
				Log.Error(ex, "Unable to delete unwanted bloom filter: {bloomFilterFile}", bloomFilterFile);
			}
			throw;
		}
	}

	private static int GetMaxOf(List<IEnumerator<IndexEntry>> enumerators) {
		var max = new IndexEntry(ulong.MinValue, 0, long.MinValue);
		int idx = 0;
		for (int i = 0; i < enumerators.Count; i++) {
			var cur = enumerators[i].Current;
			if (cur.CompareTo(max) > 0) {
				max = cur;
				idx = i;
			}
		}

		return idx;
	}

	private static unsafe void AppendRecordTo(Stream stream, byte[] buffer, byte version, IndexEntry entry,
		int indexEntrySize) {
		var bytes = entry.Bytes;
		if (version == PTableVersions.IndexV1) {
			var entryV1 = new IndexEntryV1((uint)entry.Stream, (int)entry.Version, entry.Position);
			bytes = entryV1.Bytes;
		} else if (version == PTableVersions.IndexV2) {
			var entryV2 = new IndexEntryV2(entry.Stream, (int)entry.Version, entry.Position);
			bytes = entryV2.Bytes;
		}

		Marshal.Copy((IntPtr)bytes, buffer, 0, indexEntrySize);
		stream.Write(buffer, 0, indexEntrySize);
	}

	private static void ComputeMidpoints(BufferedStream bs, FileStream fs, byte version,
		int indexEntrySize, long numIndexEntries, int requiredMidpointCount, UnmanagedMemoryAppendOnlyList<Midpoint> midpoints,
		CancellationToken ct = default(CancellationToken)) {
		int indexKeySize;
		if (version == PTableVersions.IndexV4)
			indexKeySize = IndexKeyV4Size;
		else
			throw new InvalidOperationException("Unknown PTable version: " + version);

		midpoints.Clear();
		bs.Flush();
		byte[] buffer = new byte[indexKeySize];

		var previousFileStreamPosition = fs.Position;

		long previousIndex = -1;
		IndexEntryKey previousKey = new IndexEntryKey(0, 0);

		for (int k = 0; k < requiredMidpointCount; k++) {
			ct.ThrowIfCancellationRequested();

			long index = GetMidpointIndex(k, numIndexEntries, requiredMidpointCount);
			if (index == previousIndex) {
				midpoints.Add(new Midpoint(previousKey, previousIndex));
			} else {
				fs.Seek(PTableHeader.Size + index * indexEntrySize, SeekOrigin.Begin);
				fs.Read(buffer, 0, indexKeySize);
				IndexEntryKey key = new IndexEntryKey(BitConverter.ToUInt64(buffer, 8),
					BitConverter.ToInt64(buffer, 0));
				midpoints.Add(new Midpoint(key, index));
				previousIndex = index;
				previousKey = key;
			}
		}

		fs.Seek(previousFileStreamPosition, SeekOrigin.Begin);
	}

	private static void WriteMidpointsTo(BufferedStream bs, FileStream fs, byte version, int indexEntrySize,
		byte[] buffer, long dumpedEntryCount, long numIndexEntries, long requiredMidpointCount,
		UnmanagedMemoryAppendOnlyList<Midpoint> midpoints) {
		//WRITE MIDPOINT ENTRIES

		//special case, when there is a single index entry, we need two midpoints
		if (numIndexEntries == 1 && midpoints.Count == 1) {
			midpoints.Add(new Midpoint(midpoints[0].Key, midpoints[0].ItemIndex));
		}

		var midpointsWritten = 0;
		if (dumpedEntryCount == numIndexEntries && requiredMidpointCount == midpoints.Count) {
			//if these values don't match, something is wrong
			bs.Flush();
			long fileSizeUpToMidpointEntries = GetFileSizeUpToMidpointEntries(fs.Position, midpoints.Count, version);
			fs.SetLength(fileSizeUpToMidpointEntries);
			for (var i = 0; i < midpoints.Count; i++) {
				AppendMidpointRecordTo(bs, buffer, version, midpoints[i], indexEntrySize);
			}

			midpointsWritten = midpoints.Count;
			Log.Debug("Cached {midpointsWritten} index midpoints to PTable", midpointsWritten);
		} else
			Log.Debug(
				"Not caching index midpoints to PTable due to count mismatch. Table entries: {numIndexEntries} / Dumped entries: {dumpedEntryCount}, Required midpoint count: {requiredMidpointCount} /  Actual midpoint count: {midpoints}",
				numIndexEntries, dumpedEntryCount, requiredMidpointCount, midpoints.Count);

		bs.Flush();
		fs.SetLength(fs.Position + PTableFooter.GetSize(version));
		var footerBytes = new PTableFooter(version, (uint)midpointsWritten).AsByteArray();
		bs.Write(footerBytes, 0, footerBytes.Length);
		bs.Flush();
	}

	private static void AppendMidpointRecordTo(Stream stream, byte[] buffer, byte version, Midpoint midpointEntry,
		int midpointEntrySize) {
		if (version >= PTableVersions.IndexV4) {
			ulong eventStream = midpointEntry.Key.Stream;
			long eventVersion = midpointEntry.Key.Version;
			long itemIndex = midpointEntry.ItemIndex;

			for (int i = 0; i < 8; i++) {
				buffer[i] = (byte)(eventVersion & 0xFF);
				eventVersion >>= 8;
			}

			for (int i = 0; i < 8; i++) {
				buffer[i + 8] = (byte)(eventStream & 0xFF);
				eventStream >>= 8;
			}

			for (int i = 0; i < 8; i++) {
				buffer[i + 16] = (byte)(itemIndex & 0xFF);
				itemIndex >>= 8;
			}

			stream.Write(buffer, 0, midpointEntrySize);
		}
	}

	static IEnumerator<IndexEntry> Get64bitEnumerator(ISearchTable table) {
		if (table.Version == PTableVersions.IndexV1)
			throw new InvalidOperationException("Attempted to merge or scavenge a V1 PTable");

		return table.IterateAllInOrder().GetEnumerator();
	}

	public static long GetFileSizeUpToIndexEntries(long numIndexEntries, byte version) {
		int indexEntrySize = GetIndexEntrySize(version);
		return (long)PTableHeader.Size + numIndexEntries * indexEntrySize;
	}

	public static long GetFileSizeUpToMidpointEntries(long currentPosition, long numMidpointEntries, byte version) {
		int indexEntrySize = GetIndexEntrySize(version);
		return currentPosition + numMidpointEntries * indexEntrySize;
	}

	private static int GetDepth(long indexEntriesFileSize, int minDepth) {
		minDepth = Math.Max(0, Math.Min(minDepth, 28));
		if ((2L << 28) * 4096L < indexEntriesFileSize) return 28;
		for (int i = 27; i >= minDepth; i--) {
			if ((2L << i) * 4096L < indexEntriesFileSize) {
				return i + 1;
			}
		}

		return minDepth;
	}

	private static int GetRequiredMidpointCount(long numIndexEntries, byte version, int minDepth) {
		if (numIndexEntries == 0) return 0;
		if (numIndexEntries == 1) return 2;

		int indexEntrySize = GetIndexEntrySize(version);
		var depth = GetDepth(numIndexEntries * indexEntrySize, minDepth);
		return (int)Math.Max(2L, Math.Min((long)1 << depth, numIndexEntries));
	}


	public static int GetRequiredMidpointCountCached(long numIndexEntries, byte version, int minDepth = 16) {
		if (version >= PTableVersions.IndexV4)
			return GetRequiredMidpointCount(numIndexEntries, version, minDepth);
		return 0;
	}

	public static long GetMidpointIndex(int k, long numIndexEntries, int numMidpoints) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numIndexEntries);
		ArgumentOutOfRangeException.ThrowIfNegative(k);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(k, numMidpoints);

		if (k == 0)
			return 0;

		if (k == numMidpoints - 1)
			return numIndexEntries - 1;

		// k * (numIndexEntries - 1) / (numMidpoints - 1), avoiding 64-bit integer overflows
		var (q, r) = long.DivRem(numIndexEntries - 1, numMidpoints - 1);
		return k * q + k * r / (numMidpoints - 1);
	}

	public class MidpointIndexCalculator {
		private readonly long _numIndexEntries;
		private readonly int _numMidpoints;
		private int _midpointNumber;

		public MidpointIndexCalculator(long numIndexEntries, int numMidpoints, int initialMidpointNumber = 0) {
			ArgumentOutOfRangeException.ThrowIfNegative(numIndexEntries);
			ArgumentOutOfRangeException.ThrowIfNegative(numMidpoints);

			_numIndexEntries = numIndexEntries;
			_numMidpoints = numMidpoints;
			_midpointNumber = initialMidpointNumber;
			Advance();
		}

		public long? NextMidpointIndex { get; private set; }

		public void Advance() {
			if (_numIndexEntries == 0) {
				NextMidpointIndex = null;
				return;
			}

			if (_midpointNumber >= _numMidpoints) {
				// no more midpoints
				NextMidpointIndex = null;
				return;
			}

			NextMidpointIndex = GetMidpointIndex(_midpointNumber++, _numIndexEntries, _numMidpoints);
		}
	}
}
