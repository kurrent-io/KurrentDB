// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using NUnit.Framework;
using System.Collections.Generic;

namespace EventStore.Core.Tests.Index.IndexV1;

public class corrupt_index_should : SpecificationWithDirectoryPerTestFixture {
	private const int numIndexEntries = 256;
	private const int depth = 16;

	private string ConstructPTable(byte version) {
		var memTable = new HashListMemTable(version, numIndexEntries + 1);
		for (int i = 1; i <= numIndexEntries; ++i) {
			if (version > PTableVersions.IndexV1)
				memTable.Add((ulong)i, 1, i * 1337);
			else
				memTable.Add(((ulong)i) << 32, 1, i * 1337);
		}

		string pTableFilename = GetTempFilePath();
		var pTable = PTable.FromMemtable(memTable, pTableFilename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth);
		pTable.Dispose();
		return pTableFilename;
	}

	private static void CorruptBloomFilter(string bloomFilterFile) {
		using var stream = File.Open(bloomFilterFile, FileMode.Open);
		var data = new byte[255];
		stream.Seek(0, SeekOrigin.Begin);
		data[0] = 0xFF;
		stream.Write(data, 0, 1);
	}

	private static void CorruptPTableFile(string ptableFile, byte version, string corruptionType) {
		int indexEntrySize = 0;
		if (version == PTableVersions.IndexV1)
			indexEntrySize = PTable.IndexEntryV1Size;
		else if (version == PTableVersions.IndexV2)
			indexEntrySize = PTable.IndexEntryV2Size;
		else if (version == PTableVersions.IndexV3)
			indexEntrySize = PTable.IndexEntryV3Size;
		else if (version == PTableVersions.IndexV4)
			indexEntrySize = PTable.IndexEntryV4Size;

		int indexEntryKeySize = 0;
		if (version == PTableVersions.IndexV1)
			indexEntryKeySize = PTable.IndexKeyV1Size;
		else if (version == PTableVersions.IndexV2)
			indexEntryKeySize = PTable.IndexKeyV2Size;
		else if (version == PTableVersions.IndexV3)
			indexEntryKeySize = PTable.IndexKeyV3Size;
		else if (version == PTableVersions.IndexV4)
			indexEntryKeySize = PTable.IndexKeyV4Size;

		int numMidpoints = PTable.GetRequiredMidpointCountCached(numIndexEntries, version);

		byte[] data = new byte[255];

		using (FileStream stream = File.Open(ptableFile, FileMode.Open)) {
			if (corruptionType == "footerFileType") {
				stream.Seek(PTableHeader.Size + numIndexEntries * indexEntrySize + numMidpoints * indexEntrySize,
					SeekOrigin.Begin);
				data[0] = 0xFF;
				stream.Write(data, 0, 1);
			} else if (corruptionType == "footerVersion") {
				stream.Seek(
					PTableHeader.Size + numIndexEntries * indexEntrySize + numMidpoints * indexEntrySize + 1,
					SeekOrigin.Begin);
				data[0] = 0xFF;
				stream.Write(data, 0, 1);
			} else if (corruptionType == "negativeIndexEntriesSize") {
				stream.Seek(
					PTableHeader.Size + numIndexEntries * indexEntrySize + numMidpoints * indexEntrySize + 2,
					SeekOrigin.Begin);
				data[0] = 0xFF;
				data[1] = 0xFF;
				data[2] = 0xFF;
				data[3] = 0xFF;
				stream.Write(data, 0, 4);
			} else if (corruptionType == "notMultipleIndexEntrySize") {
				var footerPosition = PTableHeader.Size + numIndexEntries * indexEntrySize +
				                     numMidpoints * indexEntrySize;
				stream.Seek(footerPosition, SeekOrigin.Begin);
				var buffer = new byte[4096];
				int read = stream.Read(buffer, 0, 4096);

				//insert a byte
				stream.Seek(footerPosition, SeekOrigin.Begin);
				data[0] = 0xFF;
				stream.Write(data, 0, 1);
				stream.Write(buffer, 0, read);
			} else if (corruptionType == "lessThan2Midpoints") {
				stream.Seek(
					PTableHeader.Size + numIndexEntries * indexEntrySize + numMidpoints * indexEntrySize + 2,
					SeekOrigin.Begin);
				//1 midpoint
				data[0] = 0x01;
				data[1] = 0x00;
				data[2] = 0x00;
				data[3] = 0x00;
				stream.Write(data, 0, 4);
			} else if (corruptionType == "moreMidpointsThanIndexEntries") {
				stream.Seek(
					PTableHeader.Size + numIndexEntries * indexEntrySize + numMidpoints * indexEntrySize + 2,
					SeekOrigin.Begin);
				//change number of midpoints to 1 more than actual number to trigger the condition
				var x = numMidpoints + 1;
				for (int i = 0; i < 4; i++) {
					data[i] = (byte)(x & 0xFF);
					x >>= 8;
				}

				stream.Write(data, 0, 4);
			} else if (corruptionType == "zeroOutMiddleEntries") {
				//zeroes out middle index entries - useful for binary search tests
				List<int> indexEntriesToCorrupt = new List<int>();
				indexEntriesToCorrupt.Add(numIndexEntries / 2 - 1);
				indexEntriesToCorrupt.Add(numIndexEntries / 2);

				foreach (int entry in indexEntriesToCorrupt) {
					stream.Seek(PTableHeader.Size + entry * indexEntrySize, SeekOrigin.Begin);
					//modify one of the index entry hashes/version
					stream.Write(data, 0, indexEntryKeySize);

					if (version >= PTableVersions.IndexV4) {
						//modify one of the midpoint entry hashes/version
						stream.Seek(PTableHeader.Size + numIndexEntries * indexEntrySize + entry * indexEntrySize,
							SeekOrigin.Begin);
						stream.Write(data, 0, indexEntryKeySize);
					}
				}
			} else if (corruptionType == "maxOutMiddleEntries") {
				//maxes out middle index entries - useful for binary search tests
				List<int> indexEntriesToCorrupt = new List<int>();
				indexEntriesToCorrupt.Add(numIndexEntries / 2 - 1);
				indexEntriesToCorrupt.Add(numIndexEntries / 2);

				for (int i = 0; i < indexEntryKeySize; i++)
					data[i] = 0xFF;

				foreach (int entry in indexEntriesToCorrupt) {
					stream.Seek(PTableHeader.Size + entry * indexEntrySize, SeekOrigin.Begin);
					//modify one of the index entry hashes/version
					stream.Write(data, 0, indexEntryKeySize);

					if (version >= PTableVersions.IndexV4) {
						//modify one of the midpoint entry hashes/version
						stream.Seek(PTableHeader.Size + numIndexEntries * indexEntrySize + entry * indexEntrySize,
							SeekOrigin.Begin);
						stream.Write(data, 0, indexEntryKeySize);
					}
				}
			} else if (corruptionType == "midpointItemIndexesNotAscendingOrder") {
				if (version >= PTableVersions.IndexV4) {
					//modify one of the midpoint item indexes
					stream.Seek(
						PTableHeader.Size + numIndexEntries * indexEntrySize + 3 * indexEntrySize - sizeof(long),
						SeekOrigin.Begin);
					stream.Write(data, 0, sizeof(long));
				}
			}
		}
	}

	private ulong GetOriginalHash(ulong stream, byte version) {
		if (version == PTableVersions.IndexV1) return stream << 32;
		else return stream;
	}

	[TestCase(PTableVersions.IndexV2, false)]
	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, false)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_multiple_of_index_entry_size(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "notMultipleIndexEntrySize");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV2, false)]
	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, false)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_midpoints_index_entries_not_in_descending_order(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "zeroOutMiddleEntries");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}


	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_midpoints_item_indexes_not_in_ascending_order(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "midpointItemIndexesNotAscendingOrder");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_range(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "zeroOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.GetRange(GetOriginalHash(numIndexEntries / 4, version), 1, 1));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_range_2(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "maxOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.GetRange(GetOriginalHash(numIndexEntries / 4, version), 1, 1));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_latest_entry(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "zeroOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		IndexEntry entry;
		Assert.Throws<MaybeCorruptIndexException>(() =>
			// changed 2 to 4 here because the corruption actually removes the stream at /2, so it isn't in the bloom filter
			pTable.TryGetLatestEntry(GetOriginalHash(numIndexEntries / 4, version), out entry));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_latest_entry_2(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "maxOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		IndexEntry entry;
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.TryGetLatestEntry(GetOriginalHash(numIndexEntries / 4, version), out entry));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_oldest_entry(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "zeroOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		IndexEntry entry;
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.TryGetOldestEntry(GetOriginalHash(numIndexEntries / 4, version), out entry));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_oldest_entry_2(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "maxOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		IndexEntry entry;
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.TryGetOldestEntry(GetOriginalHash(numIndexEntries / 4, version), out entry));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_one_value(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "zeroOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		long position;
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.TryGetOneValue(GetOriginalHash(numIndexEntries / 4, version), 1, out position));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV2, true)]
	[TestCase(PTableVersions.IndexV3, true)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_index_entries_not_descending_during_ptable_get_one_value_2(byte version,
		bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "maxOutMiddleEntries");
		//loading with a depth of 1 should load only 2 midpoints (first and last index entry)
		PTable pTable = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 1, skipIndexVerify);
		long position;
		Assert.Throws<MaybeCorruptIndexException>(() =>
			pTable.TryGetOneValue(GetOriginalHash(numIndexEntries / 4, version), 1, out position));
		pTable.Dispose();
	}

	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_on_invalid_ptable_filenumber_in_footer(byte version, bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "footerFileType");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_on_header_footer_version_mismatch(byte version, bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "footerVersion");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_negative_index_entries_size(byte version, bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "negativeIndexEntriesSize");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_less_than_2_midpoints_cached(byte version, bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "lessThan2Midpoints");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void throw_exception_if_more_midpoints_than_index_entries(byte version, bool skipIndexVerify) {
		string ptableFileName = ConstructPTable(version);
		CorruptPTableFile(ptableFileName, version, "moreMidpointsThanIndexEntries");
		Assert.Throws<CorruptIndexException>(() => PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify));
	}

	[TestCase(PTableVersions.IndexV4, false)]
	[TestCase(PTableVersions.IndexV4, true)]
	public void continue_without_corrupt_bloom_filter(byte version, bool corrupt) {
		string ptableFileName = ConstructPTable(version);

		if (corrupt)
			CorruptBloomFilter(PTable.GenBloomFilterFilename(ptableFileName));

		var table = PTable.FromFile(ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, skipIndexVerify: true);
		Assert.AreEqual(!corrupt, table.HasBloomFilter);
	}
}
