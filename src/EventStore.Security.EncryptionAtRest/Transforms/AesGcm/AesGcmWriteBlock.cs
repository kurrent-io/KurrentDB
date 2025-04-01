// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

using AesGcm = System.Security.Cryptography.AesGcm;
using BlockInfo = AesGcmBlockInfo;

public sealed class AesGcmWriteBlock : IDisposable {
	private readonly AesGcm _aesGcm;

	private readonly byte[] _block;
	private readonly Memory<byte> _header;
	private readonly Memory<byte> _data;
	private readonly Memory<byte> _tag;

	private const int NonceSize = 12;
	private readonly Memory<byte> _nonce = new byte[NonceSize];

	private readonly Func<ReadOnlyMemory<byte>, bool, CancellationToken, ValueTask> _writeBlock;

	public AesGcmWriteBlock(AesGcm aesGcm, Func<ReadOnlyMemory<byte>, bool, CancellationToken, ValueTask> writeBlock) {
		ArgumentNullException.ThrowIfNull(aesGcm);
		ArgumentNullException.ThrowIfNull(writeBlock);

		_aesGcm = aesGcm;
		_writeBlock = writeBlock;

		_block = new byte[BlockInfo.BlockSize];

		var block = _block.AsMemory();

		_header = block[..BlockInfo.HeaderSize];
		block = block[BlockInfo.HeaderSize..];

		_data = block[..BlockInfo.DataSize];
		block = block[BlockInfo.DataSize..];

		_tag = block[..BlockInfo.TagSize];
		block = block[BlockInfo.TagSize..];

		Debug.Assert(block.Length == 0);
	}

	public async ValueTask Write(int blockNumber, ReadOnlyMemory<byte> plaintext, bool updateChecksum, CancellationToken ct) {
		var dataSize = plaintext.Length;
		BinaryPrimitives.WriteUInt16LittleEndian(_header.Span, (ushort) dataSize);
		CalcNonce(blockNumber, dataSize);
		Encrypt(plaintext.Span, dataSize);
		await _writeBlock(_block, updateChecksum, ct);
	}

	private void Encrypt(ReadOnlySpan<byte> plaintext, int dataSize) {
		// note: for performance reasons, we do not clear the unused portion of _data with zeroes.
		// so, some ciphertext from the previous block may be present after the ciphertext of the current block.
		// security-wise, it doesn't matter as we have already written the previous ciphertext block to disk,
		// so we're not leaking any new information.

		_aesGcm.Encrypt(_nonce.Span, plaintext, _data.Span[..dataSize], _tag.Span);
	}

	private void CalcNonce(int blockNumber, int dataSize) {
		var uniquePosition = (long) blockNumber * BlockInfo.DataSize + dataSize;
		BinaryPrimitives.WriteInt64LittleEndian(_nonce.Span, uniquePosition);
	}

	public void Dispose() {
		_aesGcm.Dispose();
	}
}
