// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.FileNamingStrategy;

namespace EventStore.Core.TransactionLog.Chunks;

public class TFChunkEnumerator {
	private readonly IVersionedFileNamingStrategy _chunkFileNamingStrategy;
	private string[] _allFiles;
	private readonly Dictionary<string, int> _nextChunkNumber;

	public TFChunkEnumerator(IVersionedFileNamingStrategy chunkFileNamingStrategy) {
		_chunkFileNamingStrategy = chunkFileNamingStrategy;
		_allFiles = null;
		_nextChunkNumber = new Dictionary<string, int>();
	}

	public IEnumerable<TFChunkInfo> EnumerateChunks(int lastChunkNumber,
		Func<(string fileName, int fileStartNumber, int fileVersion), int> getNextChunkNumber = null) {
		getNextChunkNumber ??= GetNextChunkNumber;

		if (_allFiles == null) {
			var allFiles = _chunkFileNamingStrategy.GetAllPresentFiles();
			Array.Sort(allFiles, StringComparer.CurrentCultureIgnoreCase);
			_allFiles = allFiles;
		}

		int expectedChunkNumber = 0;
		for (int i = 0; i < _allFiles.Length; i++) {
			var chunkFileName = _allFiles[i];
			var chunkNumber = _chunkFileNamingStrategy.GetIndexFor(Path.GetFileName(_allFiles[i]));
			var nextChunkNumber = -1;
			if (i + 1 < _allFiles.Length)
				nextChunkNumber = _chunkFileNamingStrategy.GetIndexFor(Path.GetFileName(_allFiles[i + 1]));

			if (chunkNumber < expectedChunkNumber) { // present in an earlier, merged, chunk
				yield return new OldVersion(chunkFileName, chunkNumber);
				continue;
			}

			if (chunkNumber > expectedChunkNumber) { // one or more chunks are missing
				for (int j = expectedChunkNumber; j < chunkNumber; j++) {
					yield return new MissingVersion(_chunkFileNamingStrategy.GetFilenameFor(j, 0), j);
				}
				// set the expected chunk number to prevent calling onFileMissing() again for the same chunk numbers
				expectedChunkNumber = chunkNumber;
			}

			if (chunkNumber == nextChunkNumber) { // there is a newer version of this chunk
				yield return new OldVersion(chunkFileName, chunkNumber);
			} else { // latest version of chunk with the expected chunk number
				expectedChunkNumber = getNextChunkNumber((chunkFileName, chunkNumber, _chunkFileNamingStrategy.GetVersionFor(Path.GetFileName(chunkFileName))));
				yield return new LatestVersion(chunkFileName, chunkNumber, expectedChunkNumber - 1);
			}
		}

		for (int i = expectedChunkNumber; i <= lastChunkNumber; i++) {
			yield return new MissingVersion(_chunkFileNamingStrategy.GetFilenameFor(i, 0), i);
		}
	}

	private int GetNextChunkNumber((string chunkFileName, int chunkNumber, int chunkVersion) t) {
		if (t.chunkVersion == 0)
			return t.chunkNumber + 1;

		// we only cache next chunk numbers for chunks having a non-zero version
		if (_nextChunkNumber.TryGetValue(t.chunkFileName, out var nextChunkNumber))
			return nextChunkNumber;

		var header = ReadChunkHeader(t.chunkFileName);
		_nextChunkNumber[t.chunkFileName] = header.ChunkEndNumber + 1;
		return header.ChunkEndNumber + 1;
	}

	private static ChunkHeader ReadChunkHeader(string chunkFileName) {
		ChunkHeader chunkHeader;
		using (var fs = new FileStream(chunkFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
			if (fs.Length < ChunkFooter.Size + ChunkHeader.Size) {
				throw new CorruptDatabaseException(new BadChunkInDatabaseException(
					$"Chunk file '{chunkFileName}' is bad. It does not have enough size for header and footer. File size is {fs.Length} bytes."));
			}
			chunkHeader = ChunkHeader.FromStream(fs);
		}

		return chunkHeader;
	}
}
