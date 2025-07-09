// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Transforms;

public interface IChunkTransformFactory {
	TransformType Type { get; }

	int TransformDataPosition(int dataPosition);

	void CreateTransformHeader(Span<byte> transformHeader);

	ValueTask ReadTransformHeader(Stream stream, Memory<byte> transformHeader, CancellationToken token = default);

	IChunkTransform CreateTransform(ReadOnlySpan<byte> transformHeader);

	int TransformHeaderLength { get; }
}
