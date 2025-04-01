// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogV2;
using EventStore.Core.Services.Archive;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Transforms;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

[Collection("ArchiveStorageTests")]
public sealed class RemoteFileSystemTests : ArchiveStorageTestsBase<RemoteFileSystemTests> {
	// this is an integration test to check that the chunks are able to read through to remote storage
	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task chunk_can_read_record_from_object_storage(StorageType storageType) {
		const int recordsCount = 10;
		const int logicalChunkNumber = 42;

		var archive = CreateSut(storageType);

		// setup local chunk first
		var localChunkName = "my-chunk";
		var chunkLocalPath = Path.Combine(DbPath, localChunkName);
		IReadOnlyList<ILogRecord> expectedRecords;
		using (var localChunk = await TFChunkHelper.CreateNewChunk(chunkLocalPath)) {
			expectedRecords = await WriteRecords(recordsCount, localChunk);

			await localChunk.Complete(CancellationToken.None);
		}

		// upload the chunk
		await using (var localChunk = new FakeChunkBlob(chunkLocalPath, logicalChunkNumber, 0, FileMode.Open)) {
			await archive.StoreChunk(localChunk, CancellationToken.None);
		}

		// read the remote chunk
		var codec = new PrefixingLocatorCodec();
		var remoteChunkName = codec.EncodeRemote(logicalChunkNumber);
		var fs = new FileSystemWithArchive(chunkSize: 4096, codec, new ChunkLocalFileSystem(DbPath), archive);
		var actualRecords = new List<ILogRecord>(recordsCount);
		using var remoteChunk = await TFChunk.FromCompletedFile(
			fs, remoteChunkName, verifyHash: false,
			reduceFileCachePressure: false,
			unbufferedRead: false, tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: DbTransformManager.Default);

		var logPosition = 0L;
		for (var i = 0; i < recordsCount; i++) {
			var result = await remoteChunk.TryReadClosestForward(logPosition, CancellationToken.None);
			Assert.True(result.Success);
			actualRecords.Add(result.LogRecord);
			logPosition = result.NextPosition;
		}

		Assert.Equal<ILogRecord>(expectedRecords, actualRecords);
	}

	[Theory]
	[StorageData.S3]
	[StorageData.FileSystem]
	public async Task chunk_can_bulk_read_record_from_object_storage(StorageType storageType) {
		const int recordsCount = 10;
		const int logicalChunkNumber = 43;

		var archive = CreateSut(storageType);

		// setup local chunk first
		var localChunkName = "my-chunk";
		var chunkLocalPath = Path.Combine(DbPath, localChunkName);
		long payloadSize;
		using (var localChunk = await TFChunkHelper.CreateNewChunk(chunkLocalPath)) {
			var records = await WriteRecords(recordsCount, localChunk);
			payloadSize = records.Aggregate(0L,
				static (size, record) => size + record.GetSizeWithLengthPrefixAndSuffix());

			await localChunk.Complete(CancellationToken.None);
		}

		// upload the chunk
		await using (var localChunk = new FakeChunkBlob(chunkLocalPath, logicalChunkNumber, 0, FileMode.Open)) {
			await archive.StoreChunk(localChunk, CancellationToken.None);
		}

		// read the remote chunk
		var codec = new PrefixingLocatorCodec();
		var remoteChunkName = codec.EncodeRemote(logicalChunkNumber);
		var fs = new FileSystemWithArchive(chunkSize: 4096, codec, new ChunkLocalFileSystem(DbPath), archive);
		using var remoteChunk = await TFChunk.FromCompletedFile(
			fs, remoteChunkName, verifyHash: false,
			reduceFileCachePressure: false,
			unbufferedRead: false, tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: DbTransformManager.Default);

		// make sure that chunks are equivalent
		using (var localChunk = File.OpenHandle(chunkLocalPath, options: FileOptions.Asynchronous)) {
			using var remoteReader = await remoteChunk.AcquireDataReader(CancellationToken.None);
			Assert.False(remoteReader.IsMemory);
			remoteReader.SetPosition(0L);

			var expected = new byte[payloadSize];
			await RandomAccess.ReadAsync(localChunk, expected, fileOffset: ChunkHeader.Size);

			var actual = new byte[payloadSize];
			await remoteReader.ReadNextBytes(actual, CancellationToken.None);

			Assert.Equal(expected, actual);
		}
	}

	private static async ValueTask<IReadOnlyList<ILogRecord>> WriteRecords(int count, TFChunk chunk, CancellationToken token = default) {
		var records = new ILogRecord[count];

		var recordFactory = new LogV2RecordFactory();
		var logPosition = 0L;
		for (var i = 0; i < count; i++) {
			var record = records[i] = recordFactory.CreatePrepare(
				logPosition: logPosition,
				correlationId: Guid.NewGuid(),
				eventId: Guid.NewGuid(),
				transactionPosition: logPosition,
				transactionOffset: 0,
				eventStreamId: "my-stream",
				expectedVersion: i,
				timeStamp: DateTime.Now,
				flags: PrepareFlags.SingleWrite,
				eventType: "my-event-type",
				data: Encoding.UTF8.GetBytes($"my-data-{i}"),
				metadata: Encoding.UTF8.GetBytes($"my-metadata-{i}"));
			var result = await chunk.TryAppend(record, token);
			Assert.True(result.Success);
			logPosition = result.NewPosition;
		}

		return records;
	}
}
