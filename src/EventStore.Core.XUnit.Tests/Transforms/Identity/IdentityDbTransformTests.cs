// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EventStore.Core.Transforms.Identity;
using EventStore.Plugins.Transforms;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Transforms.Identity;

public class IdentityDbTransformTests {
	private readonly IdentityDbTransform _dbTransform;
	private readonly IChunkTransform _chunkTransform;

	public IdentityDbTransformTests() {
		_dbTransform = new IdentityDbTransform();
		_chunkTransform = _dbTransform.ChunkFactory.CreateTransform(ReadOnlySpan<byte>.Empty);
	}

	[Fact]
	public void db_transform_has_correct_type() => Assert.Equal(TransformType.Identity, _dbTransform.Type);

	[Fact]
	public void db_transform_has_correct_name() => Assert.Equal("identity", _dbTransform.Name);

	[Fact]
	public void chunk_factory_has_correct_type() => Assert.Equal(TransformType.Identity, _dbTransform.ChunkFactory.Type);

	[Fact]
	public void chunk_factory_provides_correct_header_length() => Assert.Equal(0, _dbTransform.ChunkFactory.TransformHeaderLength);

	[Fact]
	public async Task chunk_factory_reads_correct_header() {
		await _dbTransform.ChunkFactory.ReadTransformHeader(Stream.Null, Memory<byte>.Empty);
	}

	[Fact]
	public void chunk_transform_properly_transforms_reads() {
		const int dataSize = 1024;

		var data = new byte[dataSize];
		RandomNumberGenerator.Fill(data);
		using var memStream = new MemoryStream(data);

		var transformedStream = _chunkTransform.Read.TransformData(new ChunkDataReadStream(memStream));
		var transformedData = new byte[dataSize];
		transformedStream.ReadExactly(transformedData, 0, dataSize);

		Assert.True(data.SequenceEqual(transformedData));
	}

	[Fact]
	public async Task chunk_transform_properly_transforms_writes() {
		const int dataSize = 2000;
		const int footerSize = 10;
		const int alignmentSize = 1024;

		var data = new byte[dataSize];
		RandomNumberGenerator.Fill(data);

		var footer = new byte[footerSize];
		RandomNumberGenerator.Fill(footer);

		var alignedSize = GetAlignedSize(dataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - dataSize - footerSize;

		using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		var transformedData = new byte[alignedSize];
		using var stream = new MemoryStream(transformedData);
		var transformedStream = _chunkTransform.Write.TransformData(new ChunkDataWriteStream(stream, md5));

		transformedStream.Write(data);
		await _chunkTransform.Write.CompleteData(footerSize, alignmentSize);
		var fileSize = await _chunkTransform.Write.WriteFooter(footer);
		md5.AppendData(footer);

		Assert.Equal(alignedSize, fileSize);
		Assert.True(data.SequenceEqual(transformedData[..dataSize]));
		Assert.True(new byte[paddingSize].SequenceEqual(transformedData[dataSize..^footerSize]));
		Assert.True(footer.SequenceEqual(transformedData[^footerSize..]));

		Assert.Equal(MD5.HashData(transformedData), md5.GetHashAndReset());
	}

	[Fact]
	public async Task chunk_transform_properly_transforms_writes_async() {
		const int dataSize = 2000;
		const int footerSize = 10;
		const int alignmentSize = 1024;

		var data = new byte[dataSize];
		RandomNumberGenerator.Fill(data);

		var footer = new byte[footerSize];
		RandomNumberGenerator.Fill(footer);

		var alignedSize = GetAlignedSize(dataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - dataSize - footerSize;

		using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		var transformedData = new byte[alignedSize];
		using var stream = new MemoryStream(transformedData);
		var transformedStream = _chunkTransform.Write.TransformData(new ChunkDataWriteStream(stream, md5));

		await transformedStream.WriteAsync(data);
		await _chunkTransform.Write.CompleteData(footerSize, alignmentSize);
		var fileSize = await _chunkTransform.Write.WriteFooter(footer);
		md5.AppendData(footer);

		Assert.Equal(alignedSize, fileSize);
		Assert.True(data.SequenceEqual(transformedData[..dataSize]));
		Assert.True(new byte[paddingSize].SequenceEqual(transformedData[dataSize..^footerSize]));
		Assert.True(footer.SequenceEqual(transformedData[^footerSize..]));

		Assert.Equal(MD5.HashData(transformedData), md5.GetHashAndReset());
	}

	private static int GetAlignedSize(int size, int alignment) {
		if (size % alignment == 0)
			return size;

		return ((size / alignment) + 1) * alignment;
	}
}
