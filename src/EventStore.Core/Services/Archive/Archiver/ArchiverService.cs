// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using DotNext.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Serilog;

namespace EventStore.Core.Services.Archive.Archiver;

public sealed class ArchiverService :
	IHandle<SystemMessage.SystemStart>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.ChunkSwitched>,
	IHandle<ReplicationTrackingMessage.ReplicatedTo>,
	IAsyncDisposable {

	private static readonly ILogger Log = Serilog.Log.ForContext<ArchiverService>();
	private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(10);
	private readonly IArchiveStorage _archive;
	private readonly CancellationToken _lifetimeToken;
	private readonly AsyncAutoResetEvent _archivingSignal;
	private readonly IChunkRegistry<IChunkBlob> _chunkManager;
	private readonly ConcurrentBag<ChunkInfo> _switchedChunks;
	private Task _archivingTask;

	private long _replicationPosition; // volatile
	private volatile CancellationTokenSource _cts;

	public ArchiverService(
		ISubscriber mainBus,
		IArchiveStorage archiveStorage,
		IChunkRegistry<IChunkBlob> chunkManager) {
		_archive = archiveStorage;
		_cts = new();
		_lifetimeToken = _cts.Token;
		_archivingSignal = new(initialState: false);
		_chunkManager = chunkManager;
		_archivingTask = Task.CompletedTask;
		_switchedChunks = [];

		mainBus.Subscribe<SystemMessage.SystemStart>(this);
		mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(this);
		mainBus.Subscribe<SystemMessage.ChunkSwitched>(this);
		mainBus.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(this);
	}

	public void Handle(SystemMessage.SystemStart message) {
		_archivingTask = ArchiveAsync();
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		Cancel();
	}

	public void Handle(SystemMessage.ChunkSwitched message) {
		if (message.ChunkInfo.IsRemote)
			return;

		_switchedChunks.Add(message.ChunkInfo);
		_archivingSignal.Set();
	}

	public void Handle(ReplicationTrackingMessage.ReplicatedTo message) {
		_replicationPosition = long.Max(_replicationPosition, message.LogPosition);
		_archivingSignal.Set();
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task ArchiveAsync() {
		try {
			var checkpoint = await _archive.GetCheckpoint(_lifetimeToken);
			Log.Information("Archive is at checkpoint 0x{checkpoint:X}", checkpoint);
			while (!_lifetimeToken.IsCancellationRequested) {
				await ProcessSwitchedChunksAsync(checkpoint, _lifetimeToken);

				if (await _chunkManager.TryGetInitializedChunkFor(checkpoint, _lifetimeToken) is { ChunkFooter.IsCompleted: true } chunk &&
					chunk.ChunkHeader.ChunkEndPosition <= Volatile.Read(in _replicationPosition)) {
					Log.Information("Storing chunk in archive: \"{chunk}\"", chunk.ChunkLocator);
					await _archive.StoreChunk(chunk, _lifetimeToken);

					// verify checkpoint to make sure that no one changed it concurrently, e.g. by another
					// cluster that points to the same archive storage
					await ValidateCheckpointAsync(checkpoint, _lifetimeToken);
					checkpoint = chunk.ChunkHeader.ChunkEndPosition;
					Log.Information("Setting archive checkpoint to 0x{checkpoint:X}", checkpoint);
					await _archive.SetCheckpoint(checkpoint, _lifetimeToken);
				} else {
					// loop after MaxInterval even if not triggered. (e.g. replication checkpoint can move
					// before the chunks are available for us to store to the archive)
					await _archivingSignal.WaitAsync(MaxInterval, _lifetimeToken);
				}
			}
		} catch (Exception ex) when (ex is not OperationCanceledException oce || oce.CancellationToken != _lifetimeToken) {
			Log.Error(ex, "Archive process has terminated unexpectedly");
			Application.Exit(ExitCode.Error, "Archive process has terminated unexpectedly");
		}
	}

	private async ValueTask ProcessSwitchedChunksAsync(long checkpoint, CancellationToken token) {
		// process only chunks that are already in the archive, all other chunks
		// will be processed by the main loop
		while (_switchedChunks.TryTake(out var chunkInfo)) {
			var chunk = _chunkManager.UnsafeGetChunk(chunkInfo.ChunkEndNumber);
			await chunk.EnsureInitialized(token);
			if (chunk.ChunkHeader.ChunkEndPosition <= checkpoint) {
				Log.Information("Storing switched chunk in archive: \"{chunk}\"", chunk.ChunkLocator);
				await _archive.StoreChunk(chunk, token);
			}
		}
	}

	private async ValueTask ValidateCheckpointAsync(long expected, CancellationToken token) {
		var actual = await _archive.GetCheckpoint(token);
		if (expected != actual)
			Application.Exit(ExitCode.Error,
				$"Critical error: Remote and local Archive checkpoints are out of sync: expected {expected}, but actual {actual}. " +
				$"Is another cluster configured to use the same archive?");
	}

	private void Cancel() {
		if (Interlocked.Exchange(ref _cts, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}

	public async ValueTask DisposeAsync() {
		Cancel();
		try {
			await _archivingTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext |
			                                    ConfigureAwaitOptions.SuppressThrowing);
		} finally {
			_archivingSignal.Dispose();
		}
	}
}

file static class ChunkRegistry {
	public static ValueTask<IChunkBlob> TryGetInitializedChunkFor(this IChunkRegistry<IChunkBlob> registry,
		long logPosition, CancellationToken token) {
		return registry.UnsafeTryGetChunkFor(logPosition) is { } chunk
			? EnsureInitialized(chunk, token)
			: ValueTask.FromResult<IChunkBlob>(result: null);

		static async ValueTask<IChunkBlob> EnsureInitialized(IChunkBlob chunk, CancellationToken token) {
			await chunk.EnsureInitialized(token);
			return chunk;
		}
	}
}
