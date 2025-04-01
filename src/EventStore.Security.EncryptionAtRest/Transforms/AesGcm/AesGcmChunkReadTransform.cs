// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins.Transforms;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

public sealed class AesGcmChunkReadTransform(ReadOnlyMemory<byte> key, int transformHeaderSize) : IChunkReadTransform {
	public ChunkDataReadStream TransformData(ChunkDataReadStream dataStream) {
		ArgumentNullException.ThrowIfNull(dataStream);
		return new AesGcmChunkReadStream(dataStream, key.Span, transformHeaderSize);
	}
}
