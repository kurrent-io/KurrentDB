// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.Transforms;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Plugins.Transforms;
using Xunit;
using static EventStore.Core.TransactionLog.Chunks.TFChunk.TFChunk;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;
public  class TFChunkManagerTests : DirectoryPerTest<TFChunkManagerTests>{
	private readonly TFChunkManager _sut;
	private readonly ILocatorCodec _locatorCodec;
	private readonly List<Data.ChunkInfo> _onSwitched = [];

	public TFChunkManagerTests() {
		var dbConfig = TFChunkHelper.CreateDbConfig(pathName: Fixture.Directory, writerCheckpointPosition: 0);
		_locatorCodec = new PrefixingLocatorCodec();

		var fileSystem = new FileSystemWithArchive(
			chunkSize: dbConfig.ChunkSize,
			locatorCodec: _locatorCodec,
			localFileSystem: new ChunkLocalFileSystem(dbConfig.Path),
			archive: new ArchiveReaderEmptyChunks(dbConfig.ChunkSize, chunksInArchive: 20));

		_sut = new TFChunkManager(dbConfig, fileSystem,
			new TFChunkTracker.NoOp(),
			DbTransformManager.Default) {
			OnChunkSwitched = info => _onSwitched.Add(info),
		};
	}

	public override async Task DisposeAsync() {
		await _sut.TryClose(CancellationToken.None);
		await Fixture.DisposeAsync();
	}

	static ChunkHeader CreateHeader(int start, int end) {
		var header = new ChunkHeader(
			version: (byte)ChunkVersions.Transformed,
			minCompatibleVersion: (byte)ChunkVersions.Transformed,
			chunkSize: 1,
			chunkStartNumber: start,
			chunkEndNumber: end,
			isScavenged: true,
			chunkId: Guid.NewGuid(),
			transformType: TransformType.Identity);
		return header;
	}

	async ValueTask<TFChunk> AddNewChunk(int start, int end) {
		return await _sut.AddNewChunk(CreateHeader(start, end), ReadOnlyMemory<byte>.Empty, 1000, CancellationToken.None);
	}

	async ValueTask<TFChunk> CreateNewChunk(int start, int end) {
		var newChunk = await _sut.CreateTempChunk(CreateHeader(start, end), fileSize: 1000, CancellationToken.None);
		await newChunk.CompleteScavenge([], CancellationToken.None);
		return newChunk;
	}

	IAsyncEnumerable<string> ActualChunks => Enumerable
		.Range(0, _sut.ChunksCount)
		.ToAsyncEnumerable()
		.SelectAwaitWithCancellation(async (chunkNum, ct) => (await _sut.GetInitializedChunk(chunkNum, ct)).ChunkLocator);

	[Theory]
	[InlineData(1, 4, "too big")]
	[InlineData(2, 3, "too small")]
	[InlineData(2, 4, "misaligned")]
	public async Task one_chunk_replaces_no_chunks(int start, int end, string explanation) {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3),
			await AddNewChunk(4, 5),
		};

		// when
		using var newChunk = await CreateNewChunk(start, end);

		var switched = await _sut.SwitchInCompletedChunks([newChunk.ChunkLocator], CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			chunks[1], chunks[1],chunks[1],
			chunks[2], chunks[2],
		};

		Assert.False(switched);
		Assert.Equal(expectedChunks.ToAsyncEnumerable().Select(chunk => chunk.ChunkLocator), ActualChunks);
		Assert.Empty(_onSwitched);
		_ = explanation;
	}

	[Fact]
	public async Task one_chunk_replaces_one_chunk() {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3), // <-- will replace this one
			await AddNewChunk(4, 5),
		};

		// when
		using var newChunk = await CreateNewChunk(1, 3);

		var switched = await _sut.SwitchInCompletedChunks([newChunk.ChunkLocator], CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			newChunk, newChunk, newChunk,
			chunks[2], chunks[2],
		};

		Assert.True(switched);
		Assert.Equal(expectedChunks.Select(chunk => chunk.ChunkLocator), ActualChunks);
		Assert.Single(_onSwitched);
	}

	[Fact]
	public async Task one_chunk_replaces_many_chunks() {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3), // <-- will replace this one
			await AddNewChunk(4, 5), // <-- and this one
		};

		// when
		using var newChunk = await CreateNewChunk(1, 5);

		var switched = await _sut.SwitchInCompletedChunks([newChunk.ChunkLocator], CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			newChunk, newChunk, newChunk, newChunk, newChunk,
		};

		Assert.True(switched);
		Assert.Equal(expectedChunks.Select(chunk => chunk.ChunkLocator), ActualChunks);
		Assert.Single(_onSwitched);
	}

	[Fact]
	public async Task many_non_contiguous_chunks_replace_no_chunks() {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3),
			await AddNewChunk(4, 5),
		};

		// when
		var newChunks = new[] {
			_locatorCodec.EncodeRemote(1),
			_locatorCodec.EncodeRemote(3),
		};

		var switched = await _sut.SwitchInCompletedChunks(newChunks, CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			chunks[1], chunks[1],chunks[1],
			chunks[2], chunks[2],
		};

		Assert.False(switched);
		Assert.Equal(expectedChunks.Select(chunk => chunk.ChunkLocator), ActualChunks);
		Assert.Empty(_onSwitched);
	}

	[Theory]
	[InlineData(1, 4, "too big")]
	[InlineData(2, 3, "too small")]
	[InlineData(2, 4, "misaligned")]
	public async Task many_chunks_replace_no_chunks(int start, int end, string explanation) {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3),
			await AddNewChunk(4, 5),
		};

		// when
		var newChunks = Enumerable
			.Range(start, end - start + 1)
			.Select(_locatorCodec.EncodeRemote)
			.ToArray();

		var switched = await _sut.SwitchInCompletedChunks(newChunks, CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			chunks[1], chunks[1],chunks[1],
			chunks[2], chunks[2],
		};

		Assert.False(switched);
		Assert.Equal(expectedChunks.Select(chunk => chunk.ChunkLocator), ActualChunks);
		Assert.Empty(_onSwitched);
		_ = explanation;
	}

	[Fact]
	public async Task many_chunks_replace_one_chunk() {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3), // <-- will replace this one
			await AddNewChunk(4, 5),
		}.Select(chunk => chunk.ChunkLocator).ToArray();

		// when
		var newChunks = new[] {
			_locatorCodec.EncodeRemote(1),
			_locatorCodec.EncodeRemote(2),
			_locatorCodec.EncodeRemote(3),
		};

		var switched = await _sut.SwitchInCompletedChunks(newChunks, CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			newChunks[0],
			newChunks[1],
			newChunks[2],
			chunks[2], chunks[2],
		};

		Assert.True(switched);
		Assert.Equal(expectedChunks, ActualChunks);
		Assert.Equal(3, _onSwitched.Count);
	}

	[Fact]
	public async Task many_chunks_replace_many_chunks() {
		// given
		var chunks = new[] {
			await AddNewChunk(0, 0),
			await AddNewChunk(1, 3), // <-- will replace this one
			await AddNewChunk(4, 5), // <-- and this one
		}.Select(chunk => chunk.ChunkLocator).ToArray();

		// when
		var newChunks = new[] {
			_locatorCodec.EncodeRemote(1),
			_locatorCodec.EncodeRemote(2),
			_locatorCodec.EncodeRemote(3),
			_locatorCodec.EncodeRemote(4),
			_locatorCodec.EncodeRemote(5),
		};

		var switched = await _sut.SwitchInCompletedChunks(newChunks, CancellationToken.None);

		// then
		var expectedChunks = new[] {
			chunks[0],
			newChunks[0],
			newChunks[1],
			newChunks[2],
			newChunks[3],
			newChunks[4],
		};

		Assert.True(switched);
		Assert.Equal(expectedChunks, ActualChunks);
		Assert.Equal(5, _onSwitched.Count);
	}

	[Fact]
	public async Task ensure_lazy_chunk_initialized() {
		const int chunkNumber = 0;
		var expectedChunk = await FromCompletedFile(
			_sut.FileSystem,
			_locatorCodec.EncodeRemote(chunkNumber),
			verifyHash: false,
			unbufferedRead: false,
			new TFChunkTracker.NoOp(),
			DbTransformManager.Default,
			reduceFileCachePressure: false,
			chunkNumber);

		await _sut.AddChunk(expectedChunk, CancellationToken.None);
		Assert.False(expectedChunk.Initialized);

		var actualChunk = await _sut.GetInitializedChunk(chunkNumber, CancellationToken.None);
		Assert.Same(expectedChunk, actualChunk);
		Assert.True(actualChunk.Initialized);
	}
}
