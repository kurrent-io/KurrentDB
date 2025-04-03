// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Polly;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

public class ResilientArchiveStorageTests {
	private readonly IArchiveStorage _sut;
	private readonly FakeArchiveStorage _wrapped;

	public ResilientArchiveStorageTests() {
		var pipeline = new ResiliencePipelineBuilder()
			.AddRetry(new() {
				MaxRetryAttempts = 1,
				Delay = TimeSpan.FromSeconds(0),
			})
			.Build();

		_wrapped = new FakeArchiveStorage();
		_sut = new ResilientArchiveStorage(pipeline, _wrapped);
	}

	[Fact]
	public async Task can_get_checkpoint() {
		Assert.Equal(123, await _sut.GetCheckpoint(CancellationToken.None));
		Assert.Equal(2, _wrapped.Calls);
	}

	[Fact]
	public async Task can_get_metadata() {
		var metadata = await _sut.GetMetadataAsync(1, CancellationToken.None);
		Assert.Equal(1000, metadata.PhysicalSize);
		Assert.Equal(2, _wrapped.Calls);
	}

	[Fact]
	public async Task can_read() {
		Assert.Equal(10, await _sut.ReadAsync(1, Memory<byte>.Empty, 0, CancellationToken.None));
		Assert.Equal(2, _wrapped.Calls);
	}

	[Fact]
	public async Task can_set_checkpoint() {
		await _sut.SetCheckpoint(123, CancellationToken.None);
		Assert.Equal(2, _wrapped.Calls);
	}

	[Fact]
	public async Task can_store_chunk() {
		await _sut.StoreChunk(null, CancellationToken.None);
		Assert.Equal(2, _wrapped.Calls);
	}

	// throws an exception on the first call
	class FakeArchiveStorage : IArchiveStorage {
		public int Calls { get; private set; }

		private ValueTask OnCall() =>
			Calls++ == 0
				? ValueTask.FromException(new Exception("some exception"))
				: ValueTask.CompletedTask;

		public async ValueTask<long> GetCheckpoint(CancellationToken ct) {
			await OnCall();
			return 123;
		}

		public async ValueTask<ArchivedChunkMetadata> GetMetadataAsync(int logicalChunkNumber, CancellationToken token) {
			await OnCall();
			return new(PhysicalSize: logicalChunkNumber * 1000);
		}

		public async ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, long offset, CancellationToken ct) {
			await OnCall();
			return logicalChunkNumber * 10;
		}

		public async ValueTask SetCheckpoint(long checkpoint, CancellationToken ct) {
			await OnCall();
		}

		public async ValueTask StoreChunk(IChunkBlob chunk, CancellationToken ct) {
			await OnCall();
		}
	}
}
