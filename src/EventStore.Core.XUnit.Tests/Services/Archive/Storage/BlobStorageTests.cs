// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using EventStore.Core.Services.Archive;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.Services.Archive.Storage.S3;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

// some of the behavior of the blob storage implementations is covered by ArchiveStorageTests.cs
[Collection("ArchiveStorageTests")]
public class BlobStorageTests : DirectoryPerTest<BlobStorageTests> {
	protected const string AwsRegion = "eu-west-1";
	protected const string AwsBucket = "archiver-unit-tests";

	protected string ArchivePath => Path.Combine(Fixture.Directory, "archive");
	protected string LocalPath => Path.Combine(Fixture.Directory, "local");

	public BlobStorageTests() {
		Directory.CreateDirectory(ArchivePath);
		Directory.CreateDirectory(LocalPath);
	}

	IBlobStorage CreateSut(StorageType storageType) => storageType switch {
		StorageType.FileSystemDevelopmentOnly =>
			new FileSystemBlobStorage(new() {
				Path = ArchivePath
			}),
		StorageType.S3 =>
			new S3BlobStorage(new() {
				Bucket = AwsBucket,
				Region = AwsRegion,
			}),
		_ => throw new NotImplementedException(),
	};

	private async ValueTask<FileStream> CreateFile(string fileName, int fileSize) {
		var path = Path.Combine(LocalPath, fileName);
		using var content = Memory.AllocateExactly<byte>(fileSize);
		RandomNumberGenerator.Fill(content.Span);
		var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 512,
			FileOptions.Asynchronous);
		await fs.WriteAsync(content.Memory);
		fs.Position = 0L;
		return fs;
	}

	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task can_read_file_entirely(StorageType storageType) {
		var sut = CreateSut(storageType);

		// create a file and upload it
		const int fileSize = 1000;
		string localPath;
		await using (var fs = await CreateFile("local.file", fileSize)) {
			await sut.StoreAsync(fs, "output.file", CancellationToken.None);
			localPath = fs.Name;
		}

		// read the local file
		var localContent = await File.ReadAllBytesAsync(localPath);

		// read the uploaded file
		using var buffer = Memory.AllocateExactly<byte>(fileSize);
		await sut.ReadAsync("output.file", buffer.Memory, offset: 0, CancellationToken.None);

		// then
		Assert.Equal(localContent, buffer.Span);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task can_store_and_read_file_partially(StorageType storageType) {
		var sut = CreateSut(storageType);

		// create a file and upload it
		string localPath;
		await using (var fs = await CreateFile("local.file", fileSize: 1024)) {
			await sut.StoreAsync(fs, "output.file", CancellationToken.None);
			localPath = fs.Name;
		}

		// read the local file
		var localContent = await File.ReadAllBytesAsync(localPath);

		// read the uploaded file partially
		var start = localContent.Length / 2;
		var end = localContent.Length;
		var length = end - start;
		using var buffer = Memory.AllocateExactly<byte>(length);
		await sut.ReadAsync("output.file", buffer.Memory, offset: start, CancellationToken.None);

		// then
		Assert.Equal(localContent.AsSpan(start..end), buffer.Span);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task can_retrieve_metadata(StorageType storageType) {
		var sut = CreateSut(storageType);

		// create a file and upload it
		var fileSize = 345;
		await using (var fs = await CreateFile("local.file", fileSize)) {
			await sut.StoreAsync(fs, "output.file", CancellationToken.None);
		}

		// when
		var metadata = await sut.GetMetadataAsync("output.file", CancellationToken.None);

		// then
		Assert.Equal(fileSize, metadata.Size);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task read_missing_file_throws_FileNotFoundException(StorageType storageType) {
		var sut = CreateSut(storageType);

		await Assert.ThrowsAsync<FileNotFoundException>(async () => {
			await sut.ReadAsync("missing-from-archive.file", Memory<byte>.Empty, offset: 0, CancellationToken.None);
		});
	}
}
