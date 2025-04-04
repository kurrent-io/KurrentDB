// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Xunit;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;

public class PrefixingLocatorCodecTests {
	private readonly PrefixingLocatorCodec _sut = new();

	[Fact]
	public void can_encode_local() {
		Assert.Equal("chunk-123.456", _sut.EncodeLocal("chunk-123.456"));
	}

	[Fact]
	public void can_encode_remote() {
		Assert.Equal("archived-chunk-123", _sut.EncodeRemote(123));
	}

	[Fact]
	public void can_decode_local() {
		Assert.False(_sut.Decode("chunk-123.456", out _, out var fileName));
		Assert.Equal("chunk-123.456", fileName);
	}

	[Fact]
	public void can_decode_remote() {
		Assert.True(_sut.Decode("archived-chunk-123", out var chunkNumber, out _));
		Assert.Equal(123, chunkNumber);
	}
}
