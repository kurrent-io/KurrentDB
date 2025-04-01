// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;
using AesGcm = System.Security.Cryptography.AesGcm;
using BlockInfo = AesGcmBlockInfo;

public sealed class AesGcmChunkWriteStream : ChunkDataWriteStream {
	private const int ChunkHeaderSize = 128;

	private readonly AesGcmWriteBlock _block;
	private int _curBlockNumber;

	private int BlockSize { get; }
	private int DataSize { get; }
	private int TransformHeaderSize { get; }

	// plaintext of the last, incomplete, block
	private readonly Memory<byte> _lastBlock;

	// position that the stream has reached within the last plaintext block
	private int _lastBlockPos;

	// pending position that the stream needs to be set to
	private long? _positionToApply;

	private readonly IChunkReadTransform _readTransform;

	public AesGcmChunkWriteStream(
		ChunkDataWriteStream stream,
		ReadOnlySpan<byte> key,
		int transformHeaderSize,
		IChunkReadTransform readTransform) :
		base(stream.ChunkFileStream, stream.ChecksumAlgorithm) {
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentOutOfRangeException.ThrowIfNegative(transformHeaderSize);
		ArgumentNullException.ThrowIfNull(readTransform);

		var aesGcm = new AesGcm(key, BlockInfo.TagSize);
		_block = new AesGcmWriteBlock(aesGcm, WriteToFileStream);
		_curBlockNumber = 0;
		_positionToApply = null;

		BlockSize = BlockInfo.BlockSize;
		DataSize = BlockInfo.DataSize;
		TransformHeaderSize = transformHeaderSize;

		_lastBlock = new byte[BlockInfo.DataSize];
		_lastBlockPos = 0;

		_readTransform = readTransform;
	}

	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) {
		await ApplyInitialPosition(ct);

		// first, continue filling up the last plaintext block with data if it's not empty
		if (_lastBlockPos > 0) {
			var lastBlock = _lastBlock[_lastBlockPos..];
			var numCopied = Copy(buffer.Span, lastBlock.Span);

			buffer = buffer[numCopied..];
			_lastBlockPos += numCopied;

			// if the last block becomes full, write it to disk
			if (_lastBlockPos == DataSize) {
				await WriteCompleteBlock(_lastBlock, ct);
				ClearLastBlock();
			}
		}

		// now, write as many full blocks as possible directly to disk
		while (buffer.Length >= DataSize) {
			await WriteCompleteBlock(buffer[..DataSize], ct);
			buffer = buffer[DataSize..];
		}

		// finally, copy any remaining data to the last plaintext block
		if (buffer.Length > 0) {
			_lastBlockPos = Copy(buffer.Span, _lastBlock.Span);
		}
	}

	public override async Task FlushAsync(CancellationToken ct) {
		await ApplyInitialPosition(ct);

		// write the last block to disk before flushing for proper recovery on startup but don't commit the changes
		await WriteLastBlock(commit: false, ct);
		await base.FlushAsync(ct);
	}

	public override long Position {
		get => GetPosition();
		set => SetPosition(value);
	}

	public override long Length => throw new NotSupportedException();

	protected override void Dispose(bool disposing) {
		try {
			if (!disposing)
				return;

			_block.Dispose();
		} finally {
			base.Dispose(disposing);
		}
	}

	private static int Copy(ReadOnlySpan<byte> input, Span<byte> output) {
		var numToCopy = Math.Min(input.Length, output.Length);
		input[..numToCopy].CopyTo(output);
		return numToCopy;
	}

	private void ClearLastBlock() {
		_lastBlockPos = 0;
	}

	public async ValueTask WriteLastBlock(bool commit, CancellationToken ct) {
		if (_lastBlockPos == 0) {
			// the last block is empty, we don't need to write anything
			return;
		}

		if (commit) {
			PadLastBlock(); // if committing, pad the last block with zeroes as there'll be no additional data
			await WriteCompleteBlock(_lastBlock, ct);
			ClearLastBlock();
		} else {
			await WriteIncompleteBlock(_lastBlock[.._lastBlockPos], ct);
		}
	}

	private void PadLastBlock() {
		_lastBlock[_lastBlockPos..].Span.Clear();
		_lastBlockPos = DataSize;
	}

	private async ValueTask WriteCompleteBlock(ReadOnlyMemory<byte> data, CancellationToken ct) {
		await _block.Write(_curBlockNumber++, data, updateChecksum: true, ct);
	}

	private async ValueTask WriteIncompleteBlock(ReadOnlyMemory<byte> data, CancellationToken ct) {
		// write the block without:
		// 1) updating the checksum
		// 2) changing the stream position
		// 3) incrementing the block number

		var fileStreamPos = ChunkFileStream.Position;
		await _block.Write(_curBlockNumber, data, updateChecksum: false, ct);
		ChunkFileStream.Position = fileStreamPos;
	}

	private async ValueTask WriteToFileStream(ReadOnlyMemory<byte> data, bool updateChecksum, CancellationToken ct) {
		await ChunkFileStream.WriteAsync(data, ct);
		if (updateChecksum)
			ChecksumAlgorithm.AppendData(data.Span);
	}

	private long GetPosition() {
		return _positionToApply ?? UntransformPosition(base.Position) + _lastBlockPos;
	}

	private void SetPosition(long position) {
		_positionToApply = position;
	}

	private async ValueTask ApplyInitialPosition(CancellationToken ct) {
		if (_positionToApply is null)
			return;

		Debug.Assert(_curBlockNumber == 0);
		Debug.Assert(_lastBlockPos == 0);

		(_curBlockNumber, base.Position, _lastBlockPos) = TransformPosition(_positionToApply.Value);
		_positionToApply = null;

		// force the base stream's position to be set to compute the hash at the current position
		await base.WriteAsync(ReadOnlyMemory<byte>.Empty, ct);

		if (_lastBlockPos == 0)
			return;

		// create a temporary stream to decrypt the last block from the file stream without closing it
		var fileStream = new NonClosingStreamWrapper(ChunkFileStream);
		await using var stream = (AesGcmChunkReadStream) _readTransform.TransformData(new ChunkDataReadStream(fileStream));

		// note: truncation is done at block boundaries, so the block that's decrypted below may contain more data than
		// what we need. but this doesn't matter as we have already set _lastBlockPos to the correct position in the
		// plaintext block and we won't use this excessive data.
		await stream.PeekBlock(_curBlockNumber, _lastBlock, ct);
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
