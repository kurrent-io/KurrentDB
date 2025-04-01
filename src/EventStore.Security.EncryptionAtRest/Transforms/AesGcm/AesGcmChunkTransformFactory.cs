// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;
using EventStore.Security.EncryptionAtRest.Transforms.Common;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

using BlockInfo = AesGcmBlockInfo;

public sealed class AesGcmChunkTransformFactory(IReadOnlyList<MasterKey> masterKeys, int keySize) : IChunkTransformFactory {
	private const int ChunkHeaderSize = 128;
	private const int TransformHeaderSize = EncryptionHeader.Size + AesGcmHeader.Size;

	static AesGcmChunkTransformFactory() {
		ArgumentOutOfRangeException.ThrowIfLessThan(TransformHeaderSize, EncryptionHeader.Size + AesGcmHeader.Size);
	}

	public TransformType Type => TransformType.Encryption_AesGcm;
	public int TransformHeaderLength => TransformHeaderSize;

	public int TransformDataPosition(int dataPosition) {
		var numBlocks = dataPosition / BlockInfo.DataSize;
		if (dataPosition % BlockInfo.DataSize != 0)
			numBlocks++;

		return TransformHeaderSize + numBlocks * BlockInfo.BlockSize;
	}

	public void CreateTransformHeader(Span<byte> transformHeader) {
		var header = transformHeader;

		var encryptionHeader = header[..EncryptionHeader.Size];
		EncryptionHeader.Write(encryptionHeader, EncryptionHeader.CurrentVersion, masterKeys[^1]);
		header = header[EncryptionHeader.Size..];

		var aesGcmHeader = header[..AesGcmHeader.Size];
		AesGcmHeader.Write(aesGcmHeader, AesGcmHeader.CurrentVersion, keySize * 8);
	}

	public ValueTask ReadTransformHeader(Stream stream, Memory<byte> transformHeader, CancellationToken token = new CancellationToken()) {
		return stream.ReadExactlyAsync(transformHeader, token);
	}

	public IChunkTransform CreateTransform(ReadOnlySpan<byte> transformHeader) {
		var header = transformHeader;

		var encryptionHeader = header[..EncryptionHeader.Size];
		EncryptionHeader.Read(encryptionHeader, out _, out var masterKeyId, out var salt);
		var masterKey = masterKeys.FirstOrDefault(x => x.Id == masterKeyId);
		if (masterKey == default)
			throw new Exception($"Failed to load master key with ID: {masterKeyId}");
		header = header[EncryptionHeader.Size..];

		var aesGcmHeader = header[..AesGcmHeader.Size];
		AesGcmHeader.Read(aesGcmHeader, out _, out var aesKeySizeInBits);
		var dataKey = DeriveDataKey(
			dataKeySize: aesKeySizeInBits / 8,
			masterKey: masterKey.Key.Span,
			salt: salt);

		return new AesGcmChunkTransform(dataKey, TransformHeaderSize);
	}

	private static ReadOnlyMemory<byte> DeriveDataKey(int dataKeySize, ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> salt) {
		var dataKey = new byte[dataKeySize].AsMemory();

		// see: https://soatok.blog/2021/11/17/understanding-hkdf/ about why passing the salt to the info parameter
		// is important to have "KDF security" guarantees.
		HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, dataKey.Span, "AesGcm-Chunk"u8, salt);
		return dataKey;
	}
}
