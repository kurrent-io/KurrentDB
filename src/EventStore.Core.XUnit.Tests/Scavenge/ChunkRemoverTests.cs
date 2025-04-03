// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.Index.Hashers;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.InMemory;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using EventStore.Core.TransactionLog.Scavenging.Stages;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ChunkRemoverTests {
	readonly IScavengeStateBackend<string> _backend;
	readonly ScavengeStateForChunkWorker<string> _scavengeState;
	readonly FakeChunkManager _chunkManager = new();

	public ChunkRemoverTests() {
		_backend = new InMemoryScavengeBackend();
		_scavengeState = new ScavengeStateForChunkWorker<string>(
			hasher: new HumanReadableHasher(),
			backend: _backend,
			collisions: [],
			onDispose: () => { });
	}

	ChunkRemover<string, ILogRecord> GenSut(
		int retainDays,
		long retainBytes,
		Func<long> readArchiveCheckpoint) {

		var sut = new ChunkRemover<string, ILogRecord>(
			logger: Serilog.Log.Logger,
			archiveCheckpoint: new AdvancingCheckpoint(_ => new(readArchiveCheckpoint())),
			chunkManager: _chunkManager,
			locatorCodec: new PrefixingLocatorCodec(),
			retainPeriod: TimeSpan.FromDays(retainDays),
			retainBytes: retainBytes);
		return sut;
	}

	static ScavengePoint GenScavengePoint(long position, DateTime effectiveNow) {
		var scavengePoint = new ScavengePoint(
			position: position,
			eventNumber: 0,
			effectiveNow: effectiveNow,
			threshold: 0);
		return scavengePoint;
	}

	public enum ExpectedOutcome {
		Removed,
		Retained,
	}

	[Theory]
	[InlineData(1000, 10, true, ExpectedOutcome.Removed, "can be removed")]
	[InlineData(1000, 10, false, ExpectedOutcome.Retained, "retain because not present in archive")]
	[InlineData(1000, 11, true, ExpectedOutcome.Retained, "retain because of retention period (in archive)")]
	[InlineData(1000, 11, false, ExpectedOutcome.Retained, "retain because of retention period (not in archive)")]
	[InlineData(1001, 10, true, ExpectedOutcome.Retained, "retain because of retention bytes (in archive)")]
	[InlineData(1001, 10, false, ExpectedOutcome.Retained, "retain because of retention bytes (not in archive)")]
	[InlineData(1001, 11, true, ExpectedOutcome.Retained, "retain because of both (in archive)")]
	[InlineData(1001, 11, false, ExpectedOutcome.Retained, "retain because of both (not in archive)")]
	public async Task simple_cases(
		int retainBytes,
		int retainDays,
		bool isInArchive,
		ExpectedOutcome expectedOutcome,
		string name) {

		_ = name;
		var minDateInChunk = new DateTime(2024, 1, 1);
		var maxDateInChunk = new DateTime(2024, 12, 1);
		_backend.ChunkTimeStampRanges[1] = new(minDateInChunk, maxDateInChunk);

		// chunk 1 contains records with positions 1000-3000
		var chunk = new FakeChunk(chunkStartNumber: 1, chunkEndNumber: 2, chunkSize: 1_000);

		var sut = GenSut(
			retainDays: retainDays,
			retainBytes: retainBytes,
			readArchiveCheckpoint: () => isInArchive
				? chunk.ChunkEndPosition
				: chunk.ChunkEndPosition - 1);

		var when = async () => {
			var removing = await sut.StartRemovingIfNotRetained(
				scavengePoint: GenScavengePoint(
					// scavenging > 1000 bytes after the end of the chunk
					position: chunk.ChunkEndPosition + 1001,
					// scavenging > 10 days after the last record in the chunk
					effectiveNow: maxDateInChunk + TimeSpan.FromDays(10.1)),
				concurrentState: _scavengeState,
				physicalChunk: chunk,
				CancellationToken.None);
			return removing;
		};

		var removing = await when();
		if (expectedOutcome == ExpectedOutcome.Removed) {
			Assert.True(removing);
			Assert.Equal<string>(_chunkManager.InterceptedLocators, [
				"archived-chunk-1",
				"archived-chunk-2",
			]);
		} else if (expectedOutcome == ExpectedOutcome.Retained) {
			Assert.False(removing);
			Assert.Equal(_chunkManager.InterceptedLocators, []);
		} else {
			throw new InvalidOperationException();
		}
	}

	[Fact]
	public async Task when_chunk_has_no_prepares() {
		// chunk 1 contains records with positions 1000-2000
		var chunk = new FakeChunk(isRemote: false, chunkStartNumber: 1, chunkEndNumber: 1, chunkSize: 1_000);

		var sut = GenSut(
			retainDays: 10,
			retainBytes: 1000,
			readArchiveCheckpoint: () =>
				// present in archive
				chunk.ChunkEndPosition);

		var removing = await sut.StartRemovingIfNotRetained(
			scavengePoint: GenScavengePoint(
				// scavenging > 1000 bytes after the end of the chunk
				position: chunk.ChunkEndPosition + 1001,
				// doesn't matter when, no dates are populated in the ChunkTimeStampRanges
				effectiveNow: DateTime.Now),
			concurrentState: _scavengeState,
			physicalChunk: chunk,
			CancellationToken.None);

		Assert.True(removing);
	}

	class FakeChunkManager : IChunkManagerForChunkRemover {
		public IReadOnlyList<string> InterceptedLocators { get; private set; } = [];

		public ValueTask<bool> SwitchInChunks(IReadOnlyList<string> locators, CancellationToken token) {
			InterceptedLocators = locators;
			return new(true);
		}
	}

	class FakeChunk(
		int chunkStartNumber,
		int chunkEndNumber,
		int chunkSize = 1_000,
		bool isRemote = false)
		: IChunkReaderForExecutor<string, ILogRecord> {

		public string Name => $"Chunk {chunkStartNumber}-{chunkEndNumber}";

		public int FileSize => throw new NotImplementedException();

		public int ChunkStartNumber => chunkStartNumber;

		public int ChunkEndNumber => chunkEndNumber;

		public bool IsReadOnly => throw new NotImplementedException();

		public bool IsRemote => isRemote;

		public long ChunkStartPosition => chunkStartNumber * chunkSize;

		public long ChunkEndPosition => (chunkEndNumber + 1) * chunkSize;

		public IAsyncEnumerable<bool> ReadInto(
			RecordForExecutor<string, ILogRecord>.NonPrepare nonPrepare,
			RecordForExecutor<string, ILogRecord>.Prepare prepare,
			CancellationToken token) {
			throw new NotImplementedException();
		}
	}
}
