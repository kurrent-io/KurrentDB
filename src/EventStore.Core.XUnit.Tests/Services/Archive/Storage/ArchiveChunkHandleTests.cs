// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive;
using EventStore.Core.Services.Archive.Storage;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

[Collection("ArchiveStorageTests")]
public class ArchiveChunkHandleTests : ArchiveStorageTestsBase<ArchiveChunkHandleTests> {
	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task can_read_chunk_via_stream(StorageType storageType) {
		var archive = CreateSut(storageType);

		// create a chunk and upload it
		await using var chunk = await CreateLocalChunk(0, 0);
		await archive.StoreChunk(chunk, CancellationToken.None);

		// read the local chunk
		var localContent = await chunk.ReadAllBytes();

		// read the uploaded chunk
		using var sut = await ArchivedChunkHandle.OpenForReadAsync(archive, 0, CancellationToken.None);
		await using var inputStream = sut.CreateStream();
		var outputStream = new MemoryStream();
		await inputStream.CopyToAsync(outputStream, CancellationToken.None);

		// then
		Assert.Equal(localContent, outputStream.ToArray());
	}
}
