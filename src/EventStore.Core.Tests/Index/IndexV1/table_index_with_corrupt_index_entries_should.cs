// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Index;
using NUnit.Framework;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog;
using EventStore.Core.Tests.Fakes;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Exceptions;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture]
public class table_index_with_corrupt_index_entries_should : SpecificationWithDirectoryPerTestFixture {
	private TableIndex<string> _tableIndex;
	private IndexMap _indexMap;
	public const string StreamName = "stream";
	public const int NumIndexEntries = 512;

	public void ConstructTableIndexWithCorruptIndexEntries(byte version, bool skipIndexVerify,
		bool createForceVerifyFile = false) {
		var lowHasher = new XXHashUnsafe();
		var highHasher = new Murmur3AUnsafe();
		var fakeReader = new TFReaderLease(new FakeIndexReader());

		_tableIndex = new TableIndex<string>(PathName, lowHasher, highHasher, "",
			() => new HashListMemTable(version, maxSize: NumIndexEntries),
			() => fakeReader,
			version,
			int.MaxValue, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: NumIndexEntries,
			skipIndexVerify: skipIndexVerify);
		_tableIndex.Initialize(long.MaxValue);

		//create index entries
		for (int i = 1; i <= NumIndexEntries; i++) {
			_tableIndex.Add(i * 1337, StreamName, i, i * 1337);
		}

		_tableIndex.Close(false);

		//load index map to obtain ptable filenames
		_indexMap = IndexMapTestFactory.FromFile(Path.Combine(PathName, TableIndex.IndexMapFilename));
		List<string> ptableFiles = new List<string>();

		foreach (string ptableFilename in _indexMap.GetAllFilenames()) {
			ptableFiles.Add(ptableFilename);
		}

		_indexMap.Dispose(TimeSpan.FromSeconds(5));

		//corrupt ptable files
		foreach (string ptableFilename in ptableFiles) {
			CorruptPTableFile(ptableFilename, version, "zeroOutMiddleEntry");
		}

		//create force verify file if requested
		if (createForceVerifyFile) {
			using (FileStream fs = new FileStream(Path.Combine(PathName, TableIndex.ForceIndexVerifyFilename),
				FileMode.OpenOrCreate)) {
			}

			;
		}

		//load table index again
		_tableIndex = new TableIndex<string>(PathName, lowHasher, highHasher, "",
			() => new HashListMemTable(version, maxSize: NumIndexEntries),
			() => fakeReader,
			version,
			int.MaxValue, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: NumIndexEntries,
			skipIndexVerify: skipIndexVerify,
			indexCacheDepth: 8);
		_tableIndex.Initialize(long.MaxValue);
	}

	[TearDown]
	public void TearDown() {
		_tableIndex.Close();
	}

	private ulong GetOriginalHash(ulong stream, byte version) {
		if (version == PTableVersions.IndexV1)
			return stream << 32;
		else
			return stream;
	}

	private void CorruptPTableFile(string ptableFile, byte version, string corruptionType) {
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

		byte[] data = new byte[255];

		using (FileStream stream = File.Open(ptableFile, FileMode.Open)) {
			if (corruptionType == "zeroOutMiddleEntry") {
				//zeroes out the middle entry - useful for binary search tests
				List<int> indexEntriesToCorrupt = new List<int>();
				indexEntriesToCorrupt.Add(NumIndexEntries / 2 - 1);

				foreach (int entry in indexEntriesToCorrupt) {
					stream.Seek(PTableHeader.Size + entry * indexEntrySize, SeekOrigin.Begin);
					//modify one of the index entry hashes/version
					stream.Write(data, 0, indexEntryKeySize);

					if (version >= PTableVersions.IndexV4) {
						//modify one of the midpoint entry hashes/version
						stream.Seek(PTableHeader.Size + NumIndexEntries * indexEntrySize + entry * indexEntrySize,
							SeekOrigin.Begin);
						stream.Write(data, 0, indexEntryKeySize);
					}
				}
			}
		}
	}

	[TestCase(PTableVersions.IndexV2)]
	[TestCase(PTableVersions.IndexV3)]
	[TestCase(PTableVersions.IndexV4)]
	public void throws_corrupt_index_exception_if_verification_enabled(byte version) {
		//the CorruptIndexException is caught internally and should trigger an index file deletion if caught
		ConstructTableIndexWithCorruptIndexEntries(version, false);
		//index map file should be deleted if index verification fails
		Assert.False(File.Exists(Path.Combine(PathName, TableIndex.IndexMapFilename)));
		//force verify file should be cleared after rebuild/verification
		Assert.False(File.Exists(Path.Combine(PathName, TableIndex.ForceIndexVerifyFilename)));
	}

	[TestCase(PTableVersions.IndexV2)]
	[TestCase(PTableVersions.IndexV3)]
	[TestCase(PTableVersions.IndexV4)]
	public void throws_maybe_corrupt_index_exception_if_verification_disabled(byte version) {
		//the MaybeCorruptIndexException is caught internally and should trigger an index file deletion if caught

		ConstructTableIndexWithCorruptIndexEntries(version, true);
		Assert.Throws<MaybeCorruptIndexException>(() => {
			//since index entries are sorted in descending order, the corrupted index entry corresponds to NumIndexEntries/2+1
			_tableIndex.GetRange(StreamName, NumIndexEntries / 2 + 1, NumIndexEntries / 2 + 1, null).ToArray();
		});

		//force verify file should be created
		Assert.True(File.Exists(Path.Combine(PathName, TableIndex.ForceIndexVerifyFilename)));
	}

	[TestCase(PTableVersions.IndexV2)]
	[TestCase(PTableVersions.IndexV3)]
	[TestCase(PTableVersions.IndexV4)]
	public void force_verification_of_index_if_verification_disabled_but_force_verification_file_present(
		byte version) {
		//the CorruptIndexException is caught internally and should trigger an index file deletion if caught
		ConstructTableIndexWithCorruptIndexEntries(version, true, createForceVerifyFile: true);

		//index map file should be deleted if index verification fails
		Assert.False(File.Exists(Path.Combine(PathName, TableIndex.IndexMapFilename)));
		//force verify file should be cleared after rebuild/verification
		Assert.False(File.Exists(Path.Combine(PathName, TableIndex.ForceIndexVerifyFilename)));
	}
}
