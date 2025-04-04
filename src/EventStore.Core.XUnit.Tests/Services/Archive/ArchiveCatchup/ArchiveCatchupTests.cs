// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage.Exceptions;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive.ArchiveCatchup;

using ArchiveCatchup = Core.Services.Archive.ArchiveCatchup.ArchiveCatchup;

public class ArchiveCatchupTests : DirectoryPerTest<ArchiveCatchupTests> {
	private const int ChunkSize = 1000;
	private string DbPath => Path.Combine(Fixture.Directory, "db");

	public ArchiveCatchupTests() {
		Directory.CreateDirectory(DbPath);
	}

	private struct Sut {
		public ArchiveCatchup Catchup { get; init; }
		public FakeArchiveStorage Archive { get; init; }
		public ICheckpoint WriterCheckpoint { get; init; }
		public ICheckpoint ChaserCheckpoint { get; init; }
		public ICheckpoint EpochCheckpoint { get; init; }
	}

	private Sut CreateSut(
		long? dbCheckpoint = null,
		long? archiveCheckpoint = null,
		string[] dbChunks = null,
		Action<int> onGetChunk = null) {
		var namingStrategy = new VersionedPatternFileNamingStrategy(string.Empty, "chunk-");
		var archiveNamingStrategy = new ArchiveNamingStrategy(namingStrategy);

		dbCheckpoint ??= 0L;
		archiveCheckpoint ??= 0L;
		dbChunks ??= CreateChunkListForDb(dbCheckpoint.Value, namingStrategy);

		foreach (var dbChunk in dbChunks) {
			using var _ = File.CreateText(Path.Combine(DbPath, dbChunk));
		}

		var archive = new FakeArchiveStorage(
			chunkSize: ChunkSize,
			archiveCheckpoint.Value,
			onGetChunk);

		var writerCheckpoint = new InMemoryCheckpoint(dbCheckpoint.Value);
		var chaserCheckpoint = new InMemoryCheckpoint(dbCheckpoint.Value);
		var epochCheckpoint = new InMemoryCheckpoint(123);
		var catchup = new ArchiveCatchup(
			dbPath: DbPath,
			writerCheckpoint: writerCheckpoint,
			chaserCheckpoint: chaserCheckpoint,
			epochCheckpoint: epochCheckpoint,
			chunkSize: ChunkSize,
			archiveStorageReader: archive,
			namingStrategy: archiveNamingStrategy
		);

		return new Sut {
			Catchup = catchup,
			Archive = archive,
			WriterCheckpoint = writerCheckpoint,
			ChaserCheckpoint = chaserCheckpoint,
			EpochCheckpoint = epochCheckpoint
		};
	}

	private string[] CreateChunkListForDb(long checkpoint, IVersionedFileNamingStrategy namingStrategy) {
		var chunks = new List<string>();

		var numChunks = checkpoint / ChunkSize;
		if (checkpoint % ChunkSize != 0)
			numChunks++;

		for (var i = 0; i < numChunks; i++)
			chunks.Add(namingStrategy.GetFilenameFor(i, 0));

		return chunks.ToArray();
	}

	[Theory]
	[InlineData(0L, 0L)]       // db is in sync with archive, both have no data
	[InlineData(1000L, 1000L)] // db is in sync with archive, both have one chunk
	[InlineData(3000L, 3000L)] // db is in sync with archive, both have multiple chunks
	[InlineData(1005L, 0L)]    // db is ahead of archive, archive is empty
	[InlineData(1005L, 1000L)] // db is ahead of archive by some data
	[InlineData(4000L, 1000L)] // db is ahead of archive by multiple chunks
	[InlineData(4005L, 1000L)] // db is ahead of archive by multiple chunks & some data
	public async Task doesnt_catch_up_if_db_is_ahead_or_in_sync_with_archive(long dbCheckpoint, long archiveCheckpoint) {
		var sut = CreateSut(dbCheckpoint: dbCheckpoint, archiveCheckpoint: archiveCheckpoint);

		await sut.Catchup.Run(CancellationToken.None);

		Assert.Empty(sut.Archive.ChunkGets);
		Assert.Equal(dbCheckpoint, sut.WriterCheckpoint.Read());
		Assert.Equal(dbCheckpoint, sut.ChaserCheckpoint.Read());
		Assert.Equal(archiveCheckpoint, await sut.Archive.GetCheckpoint(CancellationToken.None));
	}

	[Theory]
	[InlineData(0L, 1000L)]    // db is empty, archive has a single chunk
	[InlineData(0L, 3000L)]    // db is empty, archive has multiple chunks
	[InlineData(5L, 1000L)]    // db has a single, incomplete, chunk, archive has a single chunk
	[InlineData(5L, 3000L)]    // db has a single, incomplete, chunk, archive has multiple chunks
	[InlineData(2005L, 5000L)] // db has multiple chunks (including an incomplete one), archive has multiple chunks
	[InlineData(2000L, 5000L)] // db has multiple complete chunks, archive has multiple chunks
	[InlineData(3000L, 4000L)] // db is exactly one chunk behind archive
	[InlineData(3005L, 4000L)] // db is less than one chunk behind archive
	public async Task catches_up_if_db_is_behind_archive(long dbCheckpoint, long archiveCheckpoint) {
		var sut = CreateSut(
			dbCheckpoint: dbCheckpoint,
			archiveCheckpoint: archiveCheckpoint);

		await sut.Catchup.Run(CancellationToken.None);

		var chunksToGet = new List<int>();
		for (var i = (int)(dbCheckpoint / ChunkSize); i < (int)(archiveCheckpoint / ChunkSize); i++)
			chunksToGet.Add(i);

		// there are no chunks to backup as the chunk file names downloaded from the archive don't conflict with the
		// chunk files in the database
		var chunksToBackup = Array.Empty<string>();

		await VerifyCatchUp(sut, dbCheckpoint, archiveCheckpoint, chunksToGet.ToArray(), chunksToBackup);
	}

	[Theory]
	// scenario: a scavenged chunk was replicated but the writer checkpoint wasn't moved forward yet.
	// the node died then later came back online and is now catching up with the archive.
	[InlineData(0L, 4000L, new[] { "chunk-000000.000001" }, new[] { 0, 1, 2, 3 }, new[] { "chunk-000000.000001.archive.bkup" })]
	[InlineData(1000L, 5000L, new[] { "chunk-000000.000001", "chunk-000001.000001" }, new[] { 1, 2, 3, 4 }, new[] { "chunk-000001.000001.archive.bkup" })]
	public async Task backs_up_chunks_that_are_replaced_during_catchup_with_the_archive(
		long dbCheckpoint,
		long archiveCheckpoint,
		string[] dbChunks,
		int[] expectedChunksToFetch,
		string[] expectedChunksToBackup) {

		var sut = CreateSut(
			dbCheckpoint: dbCheckpoint,
			archiveCheckpoint: archiveCheckpoint,
			dbChunks: dbChunks);

		await sut.Catchup.Run(CancellationToken.None);
		await VerifyCatchUp(sut, dbCheckpoint, archiveCheckpoint, expectedChunksToFetch, expectedChunksToBackup);
	}

	[Fact]
	public async Task retries_if_a_chunk_is_deleted_from_the_archive_when_catching_up() {
		// scenario: there is an ongoing scavenge on the archive, so while catching up,
		// chunks in the archive listing may no longer be present.

		bool exceptionThrown = false;

		var sut = CreateSut(
			dbCheckpoint: 0L,
			archiveCheckpoint: 2000L,
			onGetChunk: OnGetChunk);

		await sut.Catchup.Run(CancellationToken.None);

		Assert.Equal(2000L, sut.WriterCheckpoint.Read());

		return;

		// we just want to test retries here. for simplicity, we just throw an exception the first time chunk-1 is
		// fetched, then we don't throw on the second fetch. but in practice, it would be slightly different: the
		// archive's directory listing would change on retry.
		void OnGetChunk(int logicalChunkNumber) {
			if (logicalChunkNumber == 1 && !exceptionThrown) {
				exceptionThrown = true;
				throw new ChunkDeletedException();
			}
		}
	}

	private async Task VerifyCatchUp(Sut sut, long dbCheckpoint, long archiveCheckpoint, int[] expectedChunkGets, string[] expectedChunkBackups) {
		// fetches the expected chunks
		Assert.Equal(expectedChunkGets, sut.Archive.ChunkGets);

		// moves the writer checkpoint
		Assert.Equal(archiveCheckpoint, sut.WriterCheckpoint.Read());

		// moves the chaser checkpoint
		Assert.Equal(archiveCheckpoint, sut.ChaserCheckpoint.Read());

		// doesn't move the archive checkpoint
		Assert.Equal(archiveCheckpoint, await sut.Archive.GetCheckpoint(CancellationToken.None));

		// resets the epoch checkpoint
		Assert.Equal(-1, sut.EpochCheckpoint.Read());

		// backs up the expected chunks
		Assert.Equal(expectedChunkBackups, ListBackedUpChunks());
	}

	private IEnumerable<string> ListBackedUpChunks() {
		return Directory.EnumerateFiles(DbPath)
			.Where(x => x.EndsWith(".bkup"))
			.Select(Path.GetFileName)
			.Order();
	}
}

