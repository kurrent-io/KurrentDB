// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.WithHeader;
public class WithHeaderChunkTransformFactory : IChunkTransformFactory {
	private const int TransformHeaderSize = 133;
	private readonly byte[] _header;

	public TransformType Type => (TransformType) 0xFD;
	public int TransformDataPosition(int dataPosition) => TransformHeaderSize + dataPosition;

	public WithHeaderChunkTransformFactory() {
		_header = new byte[TransformHeaderSize];
		RandomNumberGenerator.Fill(_header);
	}

	public void CreateTransformHeader(Span<byte> transformHeader) => _header.CopyTo(transformHeader);

	public ValueTask ReadTransformHeader(Stream stream, Memory<byte> transformHeader, CancellationToken token) {
		return stream.ReadExactlyAsync(transformHeader, token);
	}

	public IChunkTransform CreateTransform(ReadOnlySpan<byte> transformHeader) =>
		new WithHeaderChunkTransform(transformHeader.Length);

	public int TransformHeaderLength => _header.Length;
}
