// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Archive.Archiver;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Archiver;

public sealed class ArchiverServiceTests : DirectoryPerTest<ArchiverServiceTests> {

	[Fact]
	public async Task chunk_order_preserved() {
		var indexDirectory = Fixture.GetFilePathFor("index");
		using var logFormat = LogFormatHelper<LogFormat.V2, string>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		var dbConfig = TFChunkHelper.CreateSizedDbConfig(Fixture.Directory, 0, chunkSize: 1024 * 1024);
		var dbCreator = await TFChunkDbCreationHelper<LogFormat.V2, string>.CreateAsync(dbConfig, logFormat);
		await using var result = await dbCreator
			.Chunk(Rec.Write(0, "test"))
			.Chunk(Rec.Write(1, "test"))
			.Chunk(Rec.Write(2, "test"))
			.CreateDb();

		var storage = new FakeArchiveStorage();

		await using (var archiver = new ArchiverService(new FakeSubscriber(), storage, result.Db.Manager)) {
			archiver.Handle(new SystemMessage.SystemStart()); // start archiving background task
			archiver.Handle(new ReplicationTrackingMessage.ReplicatedTo(result.Db.Config.WriterCheckpoint.Read()));

			var timeout = TimeSpan.FromSeconds(20);
			while (storage.NumStores < 2) {
				Assert.True(await storage.StoreChunkEvent.WaitAsync(timeout));
			}
		}

		Assert.True(storage.Checkpoint > 0L);
		Assert.Equal<int>([0, 1], storage.Chunks);
	}

	[Fact]
	public async Task switched_chunk_archived() {
		var indexDirectory = Fixture.GetFilePathFor("index");
		using var logFormat = LogFormatHelper<LogFormat.V2, string>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		var dbConfig = TFChunkHelper.CreateSizedDbConfig(Fixture.Directory, 0, chunkSize: 1024 * 1024);
		var dbCreator = await TFChunkDbCreationHelper<LogFormat.V2, string>.CreateAsync(dbConfig, logFormat);
		await using var result = await dbCreator
			.Chunk(Rec.Write(0, "test"))
			.Chunk(Rec.Write(1, "test"))
			.Chunk(Rec.Write(2, "test"))
			.CreateDb();

		var storage = new FakeArchiveStorage();
		await using (var archiver = new ArchiverService(new FakeSubscriber(), storage, result.Db.Manager)) {
			archiver.Handle(new SystemMessage.SystemStart()); // start archiving background task
			archiver.Handle(new ReplicationTrackingMessage.ReplicatedTo(result.Db.Config.WriterCheckpoint.Read()));

			// ensure that chunks archived
			var timeout = TimeSpan.FromSeconds(20);
			while (storage.NumStores < 2) {
				Assert.True(await storage.StoreChunkEvent.WaitAsync(timeout));
			}

			// triggers chunk switch
			storage.StoreChunkEvent.Reset();
			var chunk = await result.Db.Manager.GetInitializedChunk(0, CancellationToken.None);
			archiver.Handle(new SystemMessage.ChunkSwitched(chunk.ChunkInfo));
			Assert.True(await storage.StoreChunkEvent.WaitAsync(timeout));
		}

		Assert.Equal(3, storage.NumStores);
	}

	[Fact]
	public async Task switched_chunk_not_archived_when_not_replicated() {
		var indexDirectory = Fixture.GetFilePathFor("index");
		using var logFormat = LogFormatHelper<LogFormat.V2, string>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		var dbConfig = TFChunkHelper.CreateSizedDbConfig(Fixture.Directory, 0, chunkSize: 1024 * 1024);
		var dbCreator = await TFChunkDbCreationHelper<LogFormat.V2, string>.CreateAsync(dbConfig, logFormat);
		await using var result = await dbCreator
			.Chunk(Rec.Write(0, "test"))
			.Chunk(Rec.Write(1, "test"))
			.Chunk(Rec.Write(2, "test"))
			.CreateDb();

		var storage = new FakeArchiveStorage();
		await using (var archiver = new ArchiverService(new FakeSubscriber(), storage, result.Db.Manager)) {
			archiver.Handle(new SystemMessage.SystemStart()); // start archiving background task

			// archive chunk #0
			archiver.Handle(new ReplicationTrackingMessage.ReplicatedTo((await result.Db.Manager.GetInitializedChunk(0, CancellationToken.None)).ChunkHeader.ChunkEndPosition));

			// switch chunk #1
			archiver.Handle(new SystemMessage.ChunkSwitched((await result.Db.Manager.GetInitializedChunk(1, CancellationToken.None)).ChunkInfo));

			// ensure that just one chunk is archived
			var timeout = TimeSpan.FromSeconds(20);
			await storage.StoreChunkEvent.WaitAsync(timeout);
		}

		Assert.Equal(1, storage.NumStores);
		Assert.Contains(0, storage.Chunks);
	}

	[Fact]
	public async Task switched_chunk_not_archived_when_its_remote() {
		var indexDirectory = Fixture.GetFilePathFor("index");
		using var logFormat = LogFormatHelper<LogFormat.V2, string>.LogFormatFactory.Create(new() {
			IndexDirectory = indexDirectory,
		});
		var dbConfig = TFChunkHelper.CreateSizedDbConfig(Fixture.Directory, 0, chunkSize: 1024 * 1024);
		var dbCreator = await TFChunkDbCreationHelper<LogFormat.V2, string>.CreateAsync(dbConfig, logFormat);
		await using var result = await dbCreator
			.Chunk(Rec.Write(0, "test"))
			.Chunk(Rec.Write(1, "test"))
			.Chunk(Rec.Write(2, "test"))
			.CreateDb();

		var storage = new FakeArchiveStorage();
		await using (var archiver = new ArchiverService(new FakeSubscriber(), storage, result.Db.Manager)) {
			archiver.Handle(new SystemMessage.SystemStart()); // start archiving background task
			archiver.Handle(new ReplicationTrackingMessage.ReplicatedTo(result.Db.Config.WriterCheckpoint.Read()));

			// ensure that chunks archived
			var timeout = TimeSpan.FromSeconds(20);
			while (storage.NumStores < 2) {
				Assert.True(await storage.StoreChunkEvent.WaitAsync(timeout));
			}

			// triggers chunk switch
			storage.StoreChunkEvent.Reset();
			var chunk = await result.Db.Manager.GetInitializedChunk(0, CancellationToken.None);
			archiver.Handle(new SystemMessage.ChunkSwitched(chunk.ChunkInfo with { IsRemote = true }));

			await storage.StoreChunkEvent.WaitAsync(timeout: TimeSpan.FromSeconds(1));
		}

		Assert.Equal(2, storage.NumStores);
	}
}

file sealed class FakeSubscriber : ISubscriber {
	public void Subscribe<T>(IAsyncHandle<T> handler) where T : Message { }
	public void Unsubscribe<T>(IAsyncHandle<T> handler) where T : Message { }
}

file sealed class FakeArchiveStorage : IArchiveStorage {
	public readonly List<int> Chunks;
	public readonly AsyncAutoResetEvent StoreChunkEvent;
	public volatile int NumStores;

	public long Checkpoint;

	public FakeArchiveStorage(long existingCheckpoint = 0L) {
		Checkpoint = existingCheckpoint;
		Chunks = [];
		StoreChunkEvent = new(initialState: false);
	}

	public ValueTask StoreChunk(IChunkBlob chunk, CancellationToken ct) {
		Chunks.Add(chunk.ChunkHeader.ChunkStartNumber);
		Interlocked.Increment(ref NumStores);
		StoreChunkEvent.Set();
		return ValueTask.CompletedTask;
	}

	public ValueTask<long> GetCheckpoint(CancellationToken ct) {
		return ValueTask.FromResult(Checkpoint);
	}

	public ValueTask SetCheckpoint(long checkpoint, CancellationToken ct) {
		Checkpoint = checkpoint;
		return ValueTask.CompletedTask;
	}

	public ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, long offset, CancellationToken ct) {
		throw new NotImplementedException();
	}

	public ValueTask<ArchivedChunkMetadata> GetMetadataAsync(int logicalChunkNumber, CancellationToken token) {
		throw new NotImplementedException();
	}
}
