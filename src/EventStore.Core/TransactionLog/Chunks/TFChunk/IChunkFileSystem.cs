// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using Serilog;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

// Chunks can be stored in different locations (say, archive vs local) but the access still goes through
// one implementation of this interface. That implementation can compose more than one implementation
// of IBlobFileSystem interface and act as a proxy
public interface IChunkFileSystem {
	ValueTask<IChunkHandle> OpenForReadAsync(string locator, ReadOptimizationHint hint, CancellationToken token);

	ValueTask SetReadOnlyAsync(string locator, bool value, CancellationToken token);

	// do not use for chunks that may be remote
	IVersionedFileNamingStrategy LocalNamingStrategy { get; }

	// Remote means not local. It could be in any remote tier of storage, not necessarily the archive.
	// (although at the moment the archive is the only remote tier)
	bool IsRemote(string locator);

	IChunkEnumerable GetChunks();

	public interface IChunkEnumerable : IAsyncEnumerable<TFChunkInfo> {
		// It is not a filter/limit, it is used to spot missing chunks
		int LastChunkNumber { get; set; }
	}

	// it's not a flag enum
	public enum ReadOptimizationHint {
		None = 0,
		RandomAccess = 1,
		SequentialScan = 2,
	}
}

public static class ChunkFileSystem {
	private static readonly ILogger Logger = Log.ForContext(typeof(ChunkFileSystem));
	public static async ValueTask<ChunkHeader> ReadHeaderAsync(this IChunkFileSystem fileSystem, string fileName, CancellationToken token) {
		if (fileSystem.IsRemote(fileName))
			Logger.Warning("Reading remote chunk header. This is OK but should be uncommon");

		using var handle = await fileSystem.OpenForReadAsync(fileName, IChunkFileSystem.ReadOptimizationHint.None, token);

		var length = handle.Length;
		if (length < ChunkFooter.Size + ChunkHeader.Size) {
			throw new CorruptDatabaseException(new BadChunkInDatabaseException(
				$"Chunk file '{fileName}' is bad. It does not have enough size for header and footer. File size is {length} bytes."));
		}

		using var buffer = Memory.AllocateExactly<byte>(ChunkHeader.Size);
		await handle.ReadAsync(buffer.Memory, offset: 0, token);
		return new(buffer.Span);
	}

	public static async ValueTask<ChunkFooter> ReadFooterAsync(this IChunkFileSystem fileSystem, string fileName, CancellationToken token) {
		if (fileSystem.IsRemote(fileName))
			Logger.Warning("Reading remote chunk footer. This is OK but should be uncommon");

		using var handle = await fileSystem.OpenForReadAsync(fileName, IChunkFileSystem.ReadOptimizationHint.None, token);

		var length = handle.Length;
		if (length < ChunkFooter.Size + ChunkHeader.Size) {
			throw new CorruptDatabaseException(new BadChunkInDatabaseException(
				$"Chunk file '{fileName}' is bad. It does not have enough size for header and footer. File size is {length} bytes."));
		}

		using var buffer = Memory.AllocateExactly<byte>(ChunkFooter.Size);
		await handle.ReadAsync(buffer.Memory, length - ChunkFooter.Size, token);
		return new(buffer.Span);
	}
}
