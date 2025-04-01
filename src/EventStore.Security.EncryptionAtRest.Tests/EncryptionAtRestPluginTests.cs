// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using EventStore.Plugins;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;
using EventStore.Plugins.Transforms;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Security.EncryptionAtRest.Tests;

public class EncryptionAtTestPluginTests {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_collect_telemetry(bool enabled) {
		using var sut = new EncryptionAtRestPlugin();
		using var collector = PluginDiagnosticsDataCollector.Start(sut.DiagnosticsName);

		IConfigurationBuilder configBuilder = new ConfigurationBuilder();

		if (enabled)
			configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:Enabled", "true"},
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:MasterKey:File:KeyPath", "./keys"},
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:Encryption:AesGcm:Enabled", "true"},
			});

		var config = configBuilder.Build();

		var builder = WebApplication.CreateBuilder();
		builder.Services
			.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService())
			.AddSingleton<IReadOnlyList<IDbTransform>>([]);

		// when
		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		collector.CollectedEvents(sut.DiagnosticsName).Should().ContainSingle().Which
			.Data["enabled"].Should().Be(enabled);
	}

	[Theory]
	[InlineData(true, true, "ENCRYPTION_AT_REST", false)]
	[InlineData(true, false, "ENCRYPTION_AT_REST", true)]
	[InlineData(false, true, "ENCRYPTION_AT_REST", false)]
	[InlineData(false, false, "ENCRYPTION_AT_REST", false)]
	[InlineData(true, true, "NONE", true)]
	[InlineData(true, false, "NONE", true)]
	[InlineData(false, true, "NONE", false)]
	[InlineData(false, false, "NONE", false)]
	public void respects_license(bool enabled, bool licensePresent, string entitlement, bool expectedException) {
		// given
		using var sut = new EncryptionAtRestPlugin();

		IConfigurationBuilder configBuilder = new ConfigurationBuilder();

		if (enabled)
			configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:Enabled", "true"},
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:MasterKey:File:KeyPath", "./keys"},
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:Encryption:AesGcm:Enabled", "true"},
			});

		var config = configBuilder.Build();

		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton<IReadOnlyList<IDbTransform>>([]);

		var licenseService = new Fixtures.FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);

		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		if (expectedException) {
			Assert.NotNull(licenseService.RejectionException);
		} else {
			Assert.Null(licenseService.RejectionException);
		}
	}

	[Fact]
	public async Task works() {
		// given
		using var sut = new EncryptionAtRestPlugin();

		IConfigurationBuilder configBuilder = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:Enabled", "true"},
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:MasterKey:File:KeyPath", "./keys"},
				{$"{KurrentConfigurationConstants.Prefix}:EncryptionAtRest:Encryption:AesGcm:Enabled", "true"},
			});

		var config = configBuilder.Build();

		// when
		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService());
		builder.Services.AddSingleton<IReadOnlyList<IDbTransform>>([]);

		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		var transforms = app.Services.GetService<IReadOnlyList<IDbTransform>>();

		Assert.NotNull(transforms);
		Assert.Single(transforms);
		Assert.Equal(TransformType.Encryption_AesGcm, transforms[0].Type);

		var chunk = Path.GetTempFileName();
		try {
			await VerifyTransformWorks(transforms[0], chunk);
		} finally {
			File.Delete(chunk);
		}
	}

	private static async Task VerifyTransformWorks(IDbTransform dbTransform, string chunk) {
		const int dataSize = 1_000_000;
		const int chunkHeaderSize = 128; // this value is hardcoded in the transform and cannot be changed in the test
		const int footerSize = 128;
		const int alignmentSize = 4096;

		var writeHeader = new byte[chunkHeaderSize];
		var writeData = new byte[dataSize];
		var writeFooter = new byte[footerSize];

		RandomNumberGenerator.Fill(writeHeader);
		RandomNumberGenerator.Fill(writeData);
		RandomNumberGenerator.Fill(writeFooter);

		await WriteFirstPart(writeHeader, writeData[..(dataSize/3)]);
		await WriteMiddlePart(dataSize / 3, writeData[(dataSize/3)..(dataSize*2/3)]);
		var writeHash = await WriteLastPart(dataSize * 2 / 3, writeData[(dataSize*2/3)..], writeFooter);

		await VerifyReads();
		await VerifyRandomReads(numRandomReads: 1000);

		return;

		async Task WriteFirstPart(ReadOnlyMemory<byte> header, ReadOnlyMemory<byte> data) {
			var transformHeader = new byte[dbTransform.ChunkFactory.TransformHeaderLength];
			dbTransform.ChunkFactory.CreateTransformHeader(transformHeader);

			var transform = dbTransform.ChunkFactory.CreateTransform(transformHeader);

			using var fileStream = File.Open(chunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			await fileStream.WriteAsync(header);
			await fileStream.WriteAsync(transformHeader);

			using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
			checksum.AppendData(header.Span);
			checksum.AppendData(transformHeader);

			using var writeStream = transform.Write.TransformData(new ChunkDataWriteStream(fileStream, checksum));
			await WriteData(writeStream, data);
		}

		async Task WriteMiddlePart(long writeResumePosition, ReadOnlyMemory<byte> data) {
			using var fileStream = File.Open(chunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			var (transform, _) = await CreateTransform(fileStream);

			using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
			fileStream.Position = 0;
			using var writeStream = transform.Write.TransformData(new ChunkDataWriteStream(fileStream, checksum));
			writeStream.Position = chunkHeaderSize + writeResumePosition;
			await WriteData(writeStream, data);
		}

		async Task<byte[]> WriteLastPart(long writeResumePosition, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> footer) {
			using var fileStream = File.Open(chunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			var (transform, _) = await CreateTransform(fileStream);

			using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
			fileStream.Position = 0;
			using var writeStream = transform.Write.TransformData(new ChunkDataWriteStream(fileStream, checksum));
			writeStream.Position = chunkHeaderSize + writeResumePosition;
			await WriteData(writeStream, data);

			await transform.Write.CompleteData(footerSize, alignmentSize);
			await transform.Write.WriteFooter(footer);
			checksum.AppendData(footer.Span);

			return checksum.GetCurrentHash();
		}

		async Task WriteData(Stream stream, ReadOnlyMemory<byte> data) {
			while (data.Length > 0) {
				var len = Random.Shared.Next(data.Length + 1);
				await stream.WriteAsync(data[..len]);
				await stream.FlushAsync();
				data = data[len..];
			}
			await stream.FlushAsync();
		}

		async Task VerifyReads() {
			using var fileStream = File.Open(chunk, FileMode.Open, FileAccess.Read, FileShare.None);
			var fileData = new byte[fileStream.Length];
			await fileStream.ReadExactlyAsync(fileData);
			var stream = new MemoryStream(fileData);
			var (transform, header) = await CreateTransform(stream);

			var data = new byte[dataSize];
			using (var readStream = transform.Read.TransformData(new ChunkDataReadStream(stream))) {
				readStream.Position = chunkHeaderSize;
				await readStream.ReadExactlyAsync(data);
			}

			var footer = new byte[footerSize];
			fileData[^footer.Length..].AsSpan().CopyTo(footer);

			using var checksum = MD5.Create();
			checksum.ComputeHash(fileData);

			Assert.True(new FileInfo(chunk).Length % alignmentSize == 0, "file size is not aligned");
			Assert.True(header.SequenceEqual(writeHeader), "header does not match");
			Assert.True(data.SequenceEqual(writeData), "data does not match");
			Assert.True(footer.SequenceEqual(writeFooter), "footer does not match");
			Assert.True(checksum.Hash!.SequenceEqual(writeHash), "checksum does not match");
		}

		async Task VerifyRandomReads(int numRandomReads) {
			using var fileStream = File.Open(chunk, FileMode.Open, FileAccess.Read, FileShare.None);
			var fileData = new byte[fileStream.Length];
			await fileStream.ReadExactlyAsync(fileData);
			var stream = new MemoryStream(fileData);
			var (transform, _) = await CreateTransform(stream);

			using (var readStream = transform.Read.TransformData(new ChunkDataReadStream(stream))) {
				for (var i = 0; i < numRandomReads; i++) {
					var startPos = Random.Shared.Next(dataSize);
					var endPos = Math.Min(dataSize, startPos + Random.Shared.Next(10000));

					var len = endPos - startPos;
					var buffer = new byte[len];

					readStream.Position = chunkHeaderSize + startPos;

					// split the reads to cover more scenarios
					var mem = buffer.AsMemory();
					while (mem.Length > 0) {
						var slice = mem[..Random.Shared.Next(1, mem.Length + 1)];
						mem = mem[slice.Length..];
						await readStream.ReadExactlyAsync(slice);
					}

					Assert.True(buffer.SequenceEqual(writeData[startPos..endPos]), "random data read does not match");
				}
			}
		}

		async Task<(IChunkTransform transform, byte[] chunkHeader)> CreateTransform(Stream stream) {
			var chunkHeader = new byte[chunkHeaderSize];
			await stream.ReadExactlyAsync(chunkHeader);
			var transformHeader = new byte[dbTransform.ChunkFactory.TransformHeaderLength];
			await dbTransform.ChunkFactory.ReadTransformHeader(stream, transformHeader);
			return (dbTransform.ChunkFactory.CreateTransform(transformHeader), chunkHeader);
		}
	}

}
