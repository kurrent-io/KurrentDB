// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;
using EventStore.Security.EncryptionAtRest.Transforms.DataStructures;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

using AesGcm = System.Security.Cryptography.AesGcm;
using BlockInfo = AesGcmBlockInfo;

public sealed class AesGcmChunkReadStream : ChunkDataReadStream {
	private const int ChunkHeaderSize = 128;

	private readonly AesGcmReadBlock _block;
	private readonly SieveBlockCache _blockCache;

	private int _curBlockNumber;

	private int BlockSize { get; }
	private int DataSize { get; }
	private int TransformHeaderSize { get; }

	// plaintext of the last read block
	private readonly Memory<byte> _lastBlock;

	// size of the plaintext of the last read block
	private int _lastBlockDataSize;

	// position that the stream has reached within the last plaintext block
	private int _lastBlockPos;

	// pending position that the stream needs to be set to
	private long? _positionToApply;

	// keeps track of the base stream's position for performance reasons: when we read from the cache, we don't move
	// the base stream's position but we update this value instead.
	private long _basePosition;

	public AesGcmChunkReadStream(
		ChunkDataReadStream stream,
		ReadOnlySpan<byte> key,
		int transformHeaderSize) :
		base(stream.ChunkFileStream) {
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentOutOfRangeException.ThrowIfNegative(transformHeaderSize);

		var aesGcm = new AesGcm(key, BlockInfo.TagSize);
		_block = new AesGcmReadBlock(aesGcm, ReadFileStream);
		_blockCache = new SieveBlockCache(cacheSize: 15, blockDataSize: BlockInfo.DataSize);
		_curBlockNumber = 0;

		BlockSize = BlockInfo.BlockSize;
		DataSize = BlockInfo.DataSize;
		TransformHeaderSize = transformHeaderSize;

		_lastBlock = new byte[BlockInfo.DataSize];
		_lastBlockDataSize = DataSize;
		_lastBlockPos = 0;

		_basePosition = base.Position;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) {
		await ApplyPosition(ct);

		var count = buffer.Length;

		// if the last block we've read was incomplete and the read is requesting for more data than it contains,
		// then read the last block again. note that this code path is executed only when reading from the same stream
		// that we're writing to, e.g when using --mem-db on the server.
		if (_lastBlockDataSize < DataSize &&
		    _lastBlockDataSize - _lastBlockPos < count) {
			await ReadLastBlockAgain(ct);
		}

		// first, read data from the last plaintext block, if available, into the output buffer
		if (_lastBlockPos > 0) {
			var numCopied = Copy(_lastBlock.Span[_lastBlockPos.._lastBlockDataSize], buffer.Span);

			buffer = buffer[numCopied..];
			_lastBlockPos += numCopied;

			// if all data from the last plaintext block has been read, clear it
			if (_lastBlockPos == DataSize)
				ClearLastBlock();
		}

		// now, decrypt as many full blocks as possible directly into the output buffer
		while (buffer.Length >= DataSize) {
			await ReadBlock(buffer[..DataSize], ct);
			buffer = buffer[DataSize..];
		}

		// finally, decrypt one last block and copy the required amount of data to the output buffer
		if (buffer.Length > 0) {
			await ReadLastBlock(ct);
			_lastBlockPos = Copy(_lastBlock.Span[.._lastBlockDataSize], buffer.Span);
		}

		return count;
	}

	public override long Seek(long offset, SeekOrigin origin) {
		if (origin != SeekOrigin.Begin)
			throw new NotSupportedException();
		SetPosition(offset);
		return offset;
	}

	public override long Position {
		get => GetPosition();
		set => SetPosition(value);
	}

	protected override void Dispose(bool disposing) {
		try {
			if (!disposing)
				return;

			_block.Dispose();
		} finally {
			base.Dispose(disposing);
		}
	}

	public async Task PeekBlock(int blockNumber, Memory<byte> plaintext, CancellationToken ct) {
		var originalPos = _basePosition;
		var originalBlockNumber = _curBlockNumber;

		_curBlockNumber = blockNumber;
		await ReadBlock(plaintext, ct);

		_curBlockNumber = originalBlockNumber;
		_basePosition = originalPos;

		// immediately apply the pending seek as the base stream is used externally
		base.Position = _basePosition;
	}

	private static int Copy(ReadOnlySpan<byte> input, Span<byte> output) {
		var numToCopy = Math.Min(input.Length, output.Length);
		input[..numToCopy].CopyTo(output);
		return numToCopy;
	}

	private void ClearLastBlock() {
		_lastBlockPos = 0;
	}

	private async ValueTask<int> ReadBlock(Memory<byte> plaintext, CancellationToken ct) {
		var blockNumber = _curBlockNumber++;

		// first, try to obtain the block data from the cache
		if (_blockCache.TryGet(blockNumber, plaintext.Span)) {
			_basePosition += BlockSize;
			return BlockInfo.DataSize; // we cache only complete blocks
		}

		// otherwise read the block from the base stream
		var dataSize = await _block.Read(blockNumber, plaintext, ct);

		if (dataSize == BlockInfo.DataSize) // cache only complete blocks
			_blockCache.Put(blockNumber, plaintext.Span);

		return dataSize;
	}

	private async ValueTask ReadLastBlock(CancellationToken ct) {
		int retries = 20;
		do {
			try {
				_lastBlockDataSize = await ReadBlock(_lastBlock, ct);
				break;
			} catch (AuthenticationTagMismatchException) {
				// the block was either:
				// i)  updated while we were reading it (this can usually happen when using --mem-db)
				// ii) really corrupted
				if (retries-- == 0)
					throw;

				// retry
				_basePosition -= BlockSize;
				_curBlockNumber--;
			}
		} while (true);
	}

	private ValueTask ReadLastBlockAgain(CancellationToken ct) {
		_basePosition -= BlockSize;
		_curBlockNumber--;
		return ReadLastBlock(ct);
	}

	private async ValueTask ReadFileStream(Memory<byte> output, CancellationToken ct) {
		ApplyBasePosition();

		var slice = output;
		while (slice.Length > 0) {
			var read = await base.ReadAsync(slice, ct);
			if (read == 0)
				throw new EndOfStreamException();

			slice = slice[read..];
		}

		_basePosition += output.Length;
	}

	private long GetPosition() {
		if (_positionToApply is { } positionToApply)
			return positionToApply;

		var position = UntransformPosition(_basePosition);
		if (_lastBlockPos > 0) {
			position -= DataSize; // we need to subtract DataSize as we had read one additional block
			position += _lastBlockPos;
		}
		return position;
	}

	private void SetPosition(long position) {
		_positionToApply = position;
	}

	private async ValueTask ApplyPosition(CancellationToken ct) {
		if (_positionToApply is null)
			return;

		var (blockNumber, blockStartPosition, remainder) = TransformPosition(_positionToApply.Value);
		_positionToApply = null;

		// optimization to avoid a block read when we're already at the block we want to go to
		if (remainder > 0 &&
		    _lastBlockPos > 0 &&
		    _curBlockNumber == blockNumber + 1) {
			_lastBlockPos = remainder;

			// this line is required because we're using DotNext.IO.StreamSource.AsSharedStream() on the server and
			// not setting the base stream's position will cause it to have a value of zero.
			_basePosition = blockStartPosition + BlockSize;
			return;
		}

		ClearLastBlock();
		_curBlockNumber = blockNumber;
		_basePosition = blockStartPosition;

		if (remainder > 0) {
			await ReadLastBlock(ct);
			_lastBlockPos = remainder;
		}
	}

	private void ApplyBasePosition() {
		base.Position = _basePosition;
	}

	private (int blockNumber, long blockStartPosition, int remainder) TransformPosition(long position) {
		position -= ChunkHeaderSize;
		var (blockNumber, remainder) = long.DivRem(position, DataSize);
		Debug.Assert(blockNumber <= int.MaxValue);
		Debug.Assert(remainder <= int.MaxValue);

		position = blockNumber * BlockSize;
		position += ChunkHeaderSize + TransformHeaderSize;
		return ((int)blockNumber, position, (int)remainder);
	}

	private long UntransformPosition(long blockStartPosition) {
		var position = blockStartPosition;

		position -= ChunkHeaderSize + TransformHeaderSize;
		var blockNumber = (int)(position / BlockSize);
		Debug.Assert(position % BlockSize == 0);
		position = blockNumber * DataSize;
		position += ChunkHeaderSize;

		return position;
	}
}
