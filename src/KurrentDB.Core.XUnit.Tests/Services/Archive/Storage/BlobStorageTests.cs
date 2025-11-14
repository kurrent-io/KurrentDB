// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using KurrentDB.Core.Services.Archive;
using KurrentDB.Core.Services.Archive.Storage;
using KurrentDB.Core.Services.Archive.Storage.Azure;
using KurrentDB.Core.Services.Archive.Storage.Gcp;
using KurrentDB.Core.Services.Archive.Storage.S3;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Archive.Storage;

// some of the behavior of the blob storage implementations is covered by ArchiveStorageTests.cs
[Collection("ArchiveStorageTests")]
public class BlobStorageTests : DirectoryPerTest<BlobStorageTests> {
	private const string AwsRegion = "eu-west-1";
	private const string AwsBucket = "archiver-unit-tests";
	private const string GcpBucket = "archiver-unit-tests";

	private string ArchivePath => Path.Combine(Fixture.Directory, "archive");
	private string LocalPath => Path.Combine(Fixture.Directory, "local");

	public BlobStorageTests() {
		Directory.CreateDirectory(ArchivePath);
		Directory.CreateDirectory(LocalPath);
	}

	IBlobStorage CreateSut(StorageType storageType) {
		IBlobStorage storage;
		switch (storageType) {
			case StorageType.FileSystemDevelopmentOnly:
				storage = new FileSystemBlobStorage(new() {
					Path = ArchivePath
				});
				break;
			case StorageType.S3:
				storage = new S3BlobStorage(new() {
					Bucket = AwsBucket,
					Region = AwsRegion,
				});
				break;
			case StorageType.Azure:
				storage = new AzureBlobStorage(AzuriteHelpers.Options);
				AzuriteHelpers.ConfigureEnvironment();
				break;
			case StorageType.GCP:
                storage = new GcpBlobStorage(new() {
                    Bucket = GcpBucket,
                });
				break;
			default:
				throw new NotImplementedException();
		}

		return storage;
	}

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
	[StorageData.Azure]
	[StorageData.GCP]
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
		var numRead = await sut.ReadAsync("output.file", buffer.Memory, offset: 0, CancellationToken.None);

		// then
		Assert.Equal(localContent, buffer.Span);
		Assert.Equal(localContent.Length, numRead);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.Azure]
	[StorageData.GCP]
	[StorageData.FileSystem]
	public async Task can_read_file_partially(StorageType storageType) {
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
		var numRead = await sut.ReadAsync("output.file", buffer.Memory, offset: start, CancellationToken.None);

		// then
		Assert.Equal(localContent.AsSpan(start..end), buffer.Span);
		Assert.Equal(localContent.Length / 2, numRead);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.Azure]
	[StorageData.GCP]
	[StorageData.FileSystem]
	public async Task can_read_file_partially_and_past_end_of_file(StorageType storageType) {
		var sut = CreateSut(storageType);

		// create a file and upload it
		string localPath;
		await using (var fs = await CreateFile("local.file", fileSize: 1024)) {
			await sut.StoreAsync(fs, "output.file", CancellationToken.None);
			localPath = fs.Name;
		}

		// read the local file
		var localContent = await File.ReadAllBytesAsync(localPath);

		// read the uploaded file partially with a buffer that goes past the end of the file
		var start = localContent.Length / 2;
		using var buffer = Memory.AllocateExactly<byte>(localContent.Length);
		var numRead = await sut.ReadAsync("output.file", buffer.Memory, offset: start, CancellationToken.None);

		// then
		Assert.Equal(localContent.AsSpan(start..), buffer.Span[..numRead]);
		Assert.Equal(localContent.Length / 2, numRead);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.Azure]
	[StorageData.GCP]
	[StorageData.FileSystem]
	public async Task can_read_past_end_of_file(StorageType storageType) {
		var sut = CreateSut(storageType);

		// create a file and upload it
		string localPath;
		await using (var fs = await CreateFile("local.file", fileSize: 1024)) {
			await sut.StoreAsync(fs, "output.file", CancellationToken.None);
			localPath = fs.Name;
		}

		// read the local file
		var localContent = await File.ReadAllBytesAsync(localPath);

		// read past the end of the uploaded file
		var start = localContent.Length;
		using var buffer = Memory.AllocateExactly<byte>(localContent.Length);
		var numRead = await sut.ReadAsync("output.file", buffer.Memory, offset: start, CancellationToken.None);

		// then
		Assert.Equal(0, numRead);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.Azure]
	[StorageData.GCP]
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
	[StorageData.Azure]
	[StorageData.GCP]
	[StorageData.FileSystem]
	public async Task read_missing_file_throws_FileNotFoundException(StorageType storageType) {
		var sut = CreateSut(storageType);

		await Assert.ThrowsAsync<FileNotFoundException>(async () => {
			await sut.ReadAsync("missing-from-archive.file", new byte[1], offset: 0, CancellationToken.None);
		});
	}

	[Theory]
	[StorageData.S3]
	[StorageData.Azure]
	[StorageData.GCP]
	[StorageData.FileSystem]
	public async Task can_overwrite_file(StorageType storageType) {
		var sut = CreateSut(storageType);

		await using var fs1 = await CreateFile("local1.file", fileSize: 1024);
		await sut.StoreAsync(fs1, "output.file", CancellationToken.None);

		await using var fs2 = await CreateFile("local2.file", fileSize: 1024);
		await sut.StoreAsync(fs2, "output.file", CancellationToken.None);
	}
}
