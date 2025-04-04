// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.FileNamingStrategy;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

// Adds archive storage to a filesystem.
public sealed class FileSystemWithArchive : IChunkFileSystem {
	private readonly int _chunkSize;
	private readonly ILocatorCodec _locatorCodec;
	private readonly IChunkFileSystem _localFileSystem;
	private readonly IArchiveStorageReader _archive;

	public FileSystemWithArchive(
		int chunkSize,
		ILocatorCodec locatorCodec,
		IChunkFileSystem localFileSystem,
		IArchiveStorageReader archive) {

		_chunkSize = chunkSize;
		_locatorCodec = locatorCodec;
		_localFileSystem = localFileSystem;
		_archive = archive;
	}

	public IVersionedFileNamingStrategy LocalNamingStrategy =>
		_localFileSystem.LocalNamingStrategy;

	public ValueTask<IChunkHandle> OpenForReadAsync(string locator, IChunkFileSystem.ReadOptimizationHint hint,
		CancellationToken token) {
		return _locatorCodec.Decode(locator, out var chunkNumber, out var fileName)
			? _archive.OpenForReadAsync(chunkNumber, token)
			: _localFileSystem.OpenForReadAsync(fileName, hint, token);
	}

	public bool IsRemote(string locator) =>
		_locatorCodec.Decode(locator, out _, out _);

	public ValueTask SetReadOnlyAsync(string locator, bool value, CancellationToken token) {
		return _locatorCodec.Decode(locator, out _, out var fileName)
			? ValueTask.CompletedTask
			: _localFileSystem.SetReadOnlyAsync(fileName, value, token);
	}

	public IChunkFileSystem.IChunkEnumerable GetChunks() {
		return new ChunkEnumerableWithArchive(this);
	}

	sealed class ChunkEnumerableWithArchive(FileSystemWithArchive fileSystem) : IChunkFileSystem.IChunkEnumerable {
		private readonly IChunkFileSystem.IChunkEnumerable _localChunks = fileSystem._localFileSystem.GetChunks();

		public int LastChunkNumber {
			get => _localChunks.LastChunkNumber;
			set => _localChunks.LastChunkNumber = value;
		}

		public async IAsyncEnumerator<TFChunkInfo> GetAsyncEnumerator(CancellationToken token = default) {
			var archiveCheckpoint = await fileSystem._archive.GetCheckpoint(token);
			var firstChunkNotInArchive = (int)(archiveCheckpoint / fileSystem._chunkSize);

			await foreach (var chunkInfo in _localChunks.WithCancellation(token)) {
				switch (chunkInfo) {
					// replace missing local versions with latest from archive if they
					// are present there
					case MissingVersion(_, var chunkNumber) when (chunkNumber < firstChunkNotInArchive): {
						var fileName = fileSystem._locatorCodec.EncodeRemote(chunkNumber);
						yield return new LatestVersion(fileName, chunkNumber, chunkNumber);
						break;
					}

					default:
						yield return chunkInfo;
						break;
				}
			}
		}
	}
}
