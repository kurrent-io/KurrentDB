// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Plugins.Transforms;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

public sealed class AesGcmDbTransform : IDbTransform {
	public string Name => "aes-gcm";
	public TransformType Type => TransformType.Encryption_AesGcm;
	public IChunkTransformFactory ChunkFactory { get; }

	private readonly int[] _keySizesInBits = { 128, 192, 256 };

	public AesGcmDbTransform(IReadOnlyList<MasterKey> masterKeys, int keySizeInBits) {
		ArgumentOutOfRangeException.ThrowIfZero(masterKeys.Count);

		if (!_keySizesInBits.Contains(keySizeInBits))
			throw new ArgumentOutOfRangeException(nameof(keySizeInBits), $"Key size not supported: {keySizeInBits}");

		ChunkFactory = new AesGcmChunkTransformFactory(masterKeys, keySizeInBits / 8);
	}
}
