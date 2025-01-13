// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext.IO;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage.Exceptions;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace EventStore.Core.Services.Archive.Storage;

public class FileSystemReader : IArchiveStorageReader {
	protected static readonly ILogger Log = Serilog.Log.ForContext<FileSystemReader>();

	private readonly string _archivePath;
	private readonly string _archiveCheckpointFile;
	private readonly FileStreamOptions _fileStreamOptions;

	public FileSystemReader(
		FileSystemOptions options,
		IArchiveChunkNameResolver chunkNameResolver,
		string archiveCheckpointFile) {

		_archivePath = options.Path;
		ChunkNameResolver = chunkNameResolver;
		_archiveCheckpointFile = archiveCheckpointFile;

		_fileStreamOptions = new FileStreamOptions {
			Access = FileAccess.Read,
			Mode = FileMode.Open,
			Options = FileOptions.Asynchronous,
		};
	}

	public IArchiveChunkNameResolver ChunkNameResolver { get; init; }

	public ValueTask<long> GetCheckpoint(CancellationToken ct) {
		ValueTask<long> task;
		var handle = default(SafeFileHandle);
		try {
			var checkpointPath = Path.Combine(_archivePath, _archiveCheckpointFile);

			// Perf: we don't need buffered read for simple one-shot read of 8 bytes
			handle = File.OpenHandle(checkpointPath);

			Span<byte> buffer = stackalloc byte[sizeof(long)];
			if (RandomAccess.Read(handle, buffer, fileOffset: 0L) != buffer.Length)
				throw new EndOfStreamException();

			var checkpoint = BinaryPrimitives.ReadInt64LittleEndian(buffer);
			task = ValueTask.FromResult(checkpoint);
		} catch (FileNotFoundException) {
			task = ValueTask.FromResult(0L);
		} catch (Exception e) {
			task = ValueTask.FromException<long>(e);
		} finally {
			handle?.Dispose();
		}

		return task;
	}

	public async ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, int offset, CancellationToken ct) {
		try {
			var chunkFile = await ChunkNameResolver.ResolveFileName(logicalChunkNumber, ct);
			var chunkPath = Path.Combine(_archivePath, chunkFile);
			using var fileStream = File.Open(chunkPath, _fileStreamOptions);
			fileStream.Seek(offset, SeekOrigin.Begin);
			return await fileStream.ReadAsync(buffer, ct);
		} catch (FileNotFoundException) {
			throw new ChunkDeletedException();
		}
	}
}
