// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins.Transforms;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

public sealed class AesGcmChunkTransform : IChunkTransform {
	public IChunkReadTransform Read { get; }
	public IChunkWriteTransform Write { get; }

	public AesGcmChunkTransform(ReadOnlyMemory<byte> key, int transformHeaderSize) {
		ArgumentNullException.ThrowIfNull(key);
		ArgumentOutOfRangeException.ThrowIfNegative(transformHeaderSize);

		Read = new AesGcmChunkReadTransform(key, transformHeaderSize);
		Write = new AesGcmChunkWriteTransform(key, transformHeaderSize, Read);
	}
}
