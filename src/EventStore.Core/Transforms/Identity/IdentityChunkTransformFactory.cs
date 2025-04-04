// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Transforms.Identity;

public sealed class IdentityChunkTransformFactory : IChunkTransformFactory {
	public TransformType Type => TransformType.Identity;
	public int TransformDataPosition(int dataPosition) => dataPosition;
	public void CreateTransformHeader(Span<byte> transformHeader) => transformHeader.Clear();

	public ValueTask ReadTransformHeader(Stream stream, Memory<byte> transformHeader, CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;

	public IChunkTransform CreateTransform(ReadOnlySpan<byte> transformHeader) => new IdentityChunkTransform();

	public int TransformHeaderLength => 0;
}
