// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO.Compression;
using KurrentDB.Common.Compression;

namespace KurrentDB.Common.Tests.Compression;

public class Rfc1952GzipCompressionProviderTests {
	readonly Rfc1952GzipCompressionProvider _sut = new(CompressionLevel.Optimal);

	[Fact]
	public async Task empty_content_produces_valid_gzip_on_dispose_async() {
		using var output = new MemoryStream();

		await using (_sut.CreateCompressionStream(output, CompressionLevel.Optimal)) { }

		var compressed = output.ToArray();
		Assert.True(compressed.Length > 0);

		output.Position = 0;
		await using var decompressionStream = _sut.CreateDecompressionStream(output);
		using var reader = new MemoryStream();
		await decompressionStream.CopyToAsync(reader);

		Assert.Empty(reader.ToArray());
	}

	[Fact]
	public async Task non_empty_content_round_trips_async() {
		var original = "Hello, World!"u8.ToArray();

		using var output = new MemoryStream();
		await using (var compressionStream = _sut.CreateCompressionStream(output, CompressionLevel.Optimal)) {
			await compressionStream.WriteAsync(original, 0, original.Length);
		}

		output.Position = 0;
		await using var decompressionStream = _sut.CreateDecompressionStream(output);
		using var result = new MemoryStream();
		await decompressionStream.CopyToAsync(result);

		Assert.Equal(original, result.ToArray());
	}

	[Fact]
	public void zero_count_write_is_ignored() {
		using var output = new MemoryStream();
		using (var compressionStream = _sut.CreateCompressionStream(output, CompressionLevel.Optimal)) {
			compressionStream.Write(new byte[10], 0, 0);
		}

		output.Position = 0;
		using var decompressionStream = _sut.CreateDecompressionStream(output);
		using var result = new MemoryStream();
		decompressionStream.CopyTo(result);

		Assert.Empty(result.ToArray());
	}
}
