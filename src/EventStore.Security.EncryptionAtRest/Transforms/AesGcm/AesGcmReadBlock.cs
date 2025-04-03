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

public sealed class AesGcmReadBlock : IDisposable {
	private readonly AesGcm _aesGcm;

	private readonly byte[] _block;
	private readonly ReadOnlyMemory<byte> _header;
	private readonly ReadOnlyMemory<byte> _data;
	private readonly ReadOnlyMemory<byte> _tag;

	private const int NonceSize = 12;
	private readonly Memory<byte> _nonce = new byte[NonceSize];

	private readonly Func<Memory<byte>, CancellationToken, ValueTask> _readBlock;

	public AesGcmReadBlock(AesGcm aesGcm, Func<Memory<byte>, CancellationToken, ValueTask> readBlock) {
		ArgumentNullException.ThrowIfNull(aesGcm);
		ArgumentNullException.ThrowIfNull(readBlock);

		_aesGcm = aesGcm;
		_readBlock = readBlock;

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

	public async ValueTask<int> Read(int blockNumber, Memory<byte> plaintext, CancellationToken ct) {
		await _readBlock(_block, ct);
		var dataSize = BinaryPrimitives.ReadUInt16LittleEndian(_header.Span);
		CalcNonce(blockNumber, dataSize);
		Decrypt(dataSize, plaintext.Span);
		return dataSize;
	}

	private void Decrypt(int dataSize, Span<byte> plaintext) {
		_aesGcm.Decrypt(_nonce.Span, _data.Span[..dataSize], _tag.Span, plaintext[..dataSize]);
	}

	private void CalcNonce(int blockNumber, int dataSize) {
		var uniquePosition = (long) blockNumber * BlockInfo.DataSize + dataSize;
		BinaryPrimitives.WriteInt64LittleEndian(_nonce.Span, uniquePosition);
	}

	public void Dispose() {
		_aesGcm.Dispose();
	}

}
