// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.Transforms;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks;

public sealed class TFChunkDb : IAsyncDisposable {
	public readonly TFChunkDbConfig Config;
	public readonly TFChunkManager Manager;
	public readonly DbTransformManager TransformManager;

	private readonly ILogger _log;
	private readonly ITransactionFileTracker _tracker;
	private int _closed;

	public TFChunkDb(
		TFChunkDbConfig config,
		ITransactionFileTracker tracker = null,
		ILogger log = null,
		IChunkFileSystem fileSystem = null,
		DbTransformManager transformManager = null,
		Action<Data.ChunkInfo> onChunkSwitched = null) {
		Ensure.NotNull(config, "config");

		Config = config;
		TransformManager = transformManager ?? DbTransformManager.Default;
		_tracker = tracker ?? new TFChunkTracker.NoOp();
		fileSystem ??= new ChunkLocalFileSystem(config.Path);

		Manager = new TFChunkManager(Config, fileSystem, _tracker, TransformManager) {
			OnChunkSwitched = onChunkSwitched,
		};

		_log = log ?? Serilog.Log.ForContext<TFChunkDb>();
	}

	struct ChunkInfo {
		public int ChunkStartNumber;
		public string ChunkFileName;
	}

	// Gets the latest versions of all historical chunks
	private static async IAsyncEnumerable<ChunkInfo> GetHistoricalChunks(
		IChunkFileSystem.IChunkEnumerable chunkEnumerator,
		[EnumeratorCancellation] CancellationToken token) {

		await foreach (var chunkInfo in chunkEnumerator.WithCancellation(token)) {
			switch (chunkInfo) {
				case LatestVersion(var fileName, var start, _):
					if (start <= chunkEnumerator.LastChunkNumber - 1)
						yield return new ChunkInfo {
							ChunkFileName = fileName,
							ChunkStartNumber = start,
						};
					break;
				case MissingVersion(var fileName, var chunkNum):
					if (chunkNum <= chunkEnumerator.LastChunkNumber - 1)
						throw new CorruptDatabaseException(new ChunkNotFoundException(fileName));

					// fine for last chunk to be 'missing' (not created yet)
					break;
					// OldVersion: don't open old versions. they will soon be deleted
			}
		}
	}

	public async ValueTask Open(
		bool verifyHash = true,
		bool readOnly = false,
		int threads = 1,
		bool createNewChunks = true,
		CancellationToken token = default) {

		Ensure.Positive(threads, "threads");

		if (Interlocked.CompareExchange(ref _closed, 0, 0) != 0)
			throw new InvalidOperationException("Cannot reopen database after closing");

		ValidateReaderChecksumsMustBeLess(Config);
		var writerCheckpoint = Config.WriterCheckpoint.Read();

		if (Config.InMemDb) {
			if (createNewChunks)
				await Manager.AddNewChunk(token);
			return;
		}

		var lastChunkNum = (int)(writerCheckpoint / Config.ChunkSize);
		// the lastChunk num doesn't necessarily exist yet, but it has not been removed due to archiving
		// because that can only happen after the writerCheckpoint has advanced to the next chunk.
		var lastChunkVersions = Manager.FileSystem.LocalNamingStrategy.GetAllVersionsFor(lastChunkNum);
		var chunkEnumerator = Manager.FileSystem.GetChunks();

		// Open the historical chunks. New records will not be written to any of these.
		// (but the last one may need completing)
		chunkEnumerator.LastChunkNumber = lastChunkNum;
		await Parallel.ForEachAsync(GetHistoricalChunks(chunkEnumerator, token),
			new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = token },
			async (chunkInfo, token) => {
				TFChunk.TFChunk chunk;
				if (lastChunkVersions.Length == 0 &&
					(chunkInfo.ChunkStartNumber + 1) * (long)Config.ChunkSize == writerCheckpoint) {
					// The situation where the logical data size is exactly divisible by ChunkSize,
					// so it might happen that we have checkpoint indicating one more chunk should exist,
					// but the actual last chunk is (lastChunkNum-1) one and it could be not completed yet -- perfectly valid situation.
					var footer = await Manager.FileSystem.ReadFooterAsync(chunkInfo.ChunkFileName, token);
					if (footer.IsCompleted)
						chunk = await TFChunk.TFChunk.FromCompletedFile(
							Manager.FileSystem,
							filename: chunkInfo.ChunkFileName,
							verifyHash: false,
							unbufferedRead: Config.Unbuffered,
							tracker: _tracker,
							reduceFileCachePressure: Config.ReduceFileCachePressure,
							getTransformFactory: TransformManager,
							footer: footer,
							token: token);
					else {
						chunk = await TFChunk.TFChunk.FromOngoingFile(
							Manager.FileSystem,
							filename: chunkInfo.ChunkFileName,
							writePosition: Config.ChunkSize,
							unbuffered: Config.Unbuffered,
							writethrough: Config.WriteThrough,
							reduceFileCachePressure: Config.ReduceFileCachePressure,
							tracker: _tracker,
							getTransformFactory: TransformManager,
							token: token);
						// chunk is full with data, we should complete it right here
						if (!readOnly)
							await chunk.Complete(token);
					}
				} else {
					// common case
					chunk = await TFChunk.TFChunk.FromCompletedFile(
						Manager.FileSystem,
						filename: chunkInfo.ChunkFileName,
						verifyHash: false,
						unbufferedRead: Config.Unbuffered,
						reduceFileCachePressure: Config.ReduceFileCachePressure,
						tracker: _tracker,
						getTransformFactory: TransformManager,
						startNumber: chunkInfo.ChunkStartNumber,
						token: token);
				}

				// This call is thread-safe.
				await Manager.AddChunk(chunk, token);
			});

		// Open the current chunk, where new records will be written. It might not exist yet.
		if (lastChunkVersions.Length == 0) {
			var onBoundary = writerCheckpoint == (Config.ChunkSize * (long)lastChunkNum);
			if (!onBoundary)
				throw new CorruptDatabaseException(
					new ChunkNotFoundException(Manager.FileSystem.LocalNamingStrategy.GetFilenameFor(lastChunkNum, 0)));

			if (!readOnly && createNewChunks)
				await Manager.AddNewChunk(token);
		} else {
			var chunkFileName = lastChunkVersions[0];
			var chunkHeader = await Manager.FileSystem.ReadHeaderAsync(chunkFileName, token);
			var chunkLocalPos = chunkHeader.GetLocalLogPosition(writerCheckpoint);
			if (chunkHeader.IsScavenged) {
				// scavenged chunks are first replicated to a temporary file before being atomically switched in.
				// thus, the writer checkpoint can point to either the beginning or the end of a scavenged chunk.
				//
				// if it was pointing to the end of the scavenged chunk, it would be "inside" the next chunk, and we
				// wouldn't be here (as the next chunk wouldn't exist yet or would not be scavenged)
				//
				// thus, the current case is possible only when a scavenged chunk was switched in but
				// the writer checkpoint wasn't yet updated & flushed. therefore, we expect the writer checkpoint to
				// point exactly to the beginning of the scavenged chunk. (i.e chunkLocalPos = 0)

				if (chunkLocalPos != 0) {
					throw new CorruptDatabaseException(new BadChunkInDatabaseException(
						$"Chunk {chunkFileName} is corrupted. Expected local chunk position: 0 but was {chunkLocalPos}. " +
						$"Writer checkpoint: {writerCheckpoint}."));
				}

				var lastChunk = await TFChunk.TFChunk.FromCompletedFile(Manager.FileSystem, chunkFileName, verifyHash: false,
					unbufferedRead: Config.Unbuffered,
					reduceFileCachePressure: Config.ReduceFileCachePressure,
					tracker: _tracker,
					getTransformFactory: TransformManager,
					header: chunkHeader,
					token: token);

				lastChunkNum = lastChunk.ChunkHeader.ChunkEndNumber + 1;

				await Manager.AddChunk(lastChunk, token);
				if (!readOnly) {
					_log.Information(
						"Moving the writer checkpoint from {checkpoint} to {chunkEndPosition}, as it points to a scavenged chunk.",
						writerCheckpoint, lastChunk.ChunkHeader.ChunkEndPosition);
					Config.WriterCheckpoint.Write(lastChunk.ChunkHeader.ChunkEndPosition);
					Config.WriterCheckpoint.Flush();

					// as of recent versions, it's possible that a new chunk was already created as the writer checkpoint
					// is updated & flushed _after_ the new chunk is created. if that's the case, we remove it.
					var newChunk = Manager.FileSystem.LocalNamingStrategy.GetFilenameFor(lastChunkNum, 0);
					if (File.Exists(newChunk))
						RemoveFile("Removing excessive chunk: {chunk}", newChunk);

					if (createNewChunks)
						await Manager.AddNewChunk(token);
				}
			} else {
				var lastChunk = await TFChunk.TFChunk.FromOngoingFile(
					Manager.FileSystem,
					filename: chunkFileName,
					writePosition: (int)chunkLocalPos,
					unbuffered: Config.Unbuffered,
					writethrough: Config.WriteThrough,
					reduceFileCachePressure: Config.ReduceFileCachePressure,
					tracker: _tracker,
					getTransformFactory: TransformManager,
					token: token);
				await Manager.AddChunk(lastChunk, token);
			}
		}

		chunkEnumerator.LastChunkNumber = lastChunkNum;
		_log.Information("Ensuring no excessive chunks...");
		await EnsureNoExcessiveChunks(chunkEnumerator, token);
		_log.Information("Done ensuring no excessive chunks.");

		if (!readOnly) {
			_log.Information("Removing old chunk versions...");
			await RemoveOldChunksVersions(chunkEnumerator, token);
			_log.Information("Done removing old chunk versions.");

			_log.Information("Cleaning up temp files...");
			CleanUpTempFiles();
			_log.Information("Done cleaning up temp files.");
		}

		if (verifyHash && lastChunkNum > 0) {
			var preLastChunk = await Manager.GetInitializedChunk(lastChunkNum - 1, token);
			var lastBgChunkNum = preLastChunk.ChunkHeader.ChunkStartNumber;
			ThreadPool.UnsafeQueueUserWorkItem(async token => {
				for (int chunkNum = lastBgChunkNum; chunkNum >= 0;) {
					var chunk = Manager.UnsafeGetChunk(chunkNum);
					try {
						await chunk.VerifyFileHash(token);
					} catch (FileBeingDeletedException exc) {
						_log.Debug(
							"{exceptionType} exception was thrown while doing background validation of chunk {chunk}.",
							exc.GetType().Name, chunk);
						_log.Debug(
							"That's probably OK, especially if truncation was request at the same time: {e}.",
							exc.Message);
					} catch (Exception exc) {
						_log.Fatal(exc, "Verification of chunk {chunk} failed, terminating server...",
							chunk);
						var msg = string.Format("Verification of chunk {0} failed, terminating server...", chunk);
						Application.Exit(ExitCode.Error, msg);
						return;
					}

					chunkNum = chunk.ChunkInfo.ChunkStartNumber - 1;
				}
			}, token, preferLocal: false);
		}

		await Manager.EnableCaching(token);
	}

	private static void ValidateReaderChecksumsMustBeLess(TFChunkDbConfig config) {
		var current = config.WriterCheckpoint.Read();
		foreach (var checkpoint in new[] { config.ChaserCheckpoint, config.EpochCheckpoint }) {
			if (checkpoint.Read() > current)
				throw new CorruptDatabaseException(new ReaderCheckpointHigherThanWriterException(checkpoint.Name));
		}
	}

	private async ValueTask EnsureNoExcessiveChunks(
		IChunkFileSystem.IChunkEnumerable chunkEnumerator,
		CancellationToken token) {

		var extraneousFiles = new List<string>();

		await foreach (var chunkInfo in chunkEnumerator.WithCancellation(token)) {
			switch (chunkInfo) {
				case LatestVersion(var fileName, var start, var end):
					// there can be at most one excessive chunk at startup:
					// when a new chunk was created but the writer checkpoint was not yet committed and flushed
					if (start == chunkEnumerator.LastChunkNumber + 1 &&
						start == end &&
						Manager.FileSystem.LocalNamingStrategy.GetVersionFor(Path.GetFileName(fileName)) == 0)
						RemoveFile("Removing excessive chunk: {chunk}", fileName);
					else if (start > chunkEnumerator.LastChunkNumber)
						extraneousFiles.Add(fileName);
					break;
				case OldVersion(var fileName, var start):
					if (start > chunkEnumerator.LastChunkNumber)
						extraneousFiles.Add(fileName);
					break;
			}
		}

		if (!extraneousFiles.IsEmpty()) {
			throw new CorruptDatabaseException(new ExtraneousFileFoundException(
				$"Unexpected files: {string.Join(", ", extraneousFiles)}."));
		}
	}

	private async ValueTask RemoveOldChunksVersions(
		IChunkFileSystem.IChunkEnumerable chunkEnumerator,
		CancellationToken token) {

		await foreach (var chunkInfo in chunkEnumerator.WithCancellation(token)) {
			switch (chunkInfo) {
				case OldVersion(var fileName, var start):
					if (start <= chunkEnumerator.LastChunkNumber)
						RemoveFile("Removing old chunk version: {chunk}...", fileName);
					break;
			}
		}
	}

	private void CleanUpTempFiles() {
		var tempFiles = Manager.FileSystem.LocalNamingStrategy.GetAllTempFiles();
		foreach (string tempFile in tempFiles) {
			try {
				RemoveFile("Deleting temporary file {file}...", tempFile);
			} catch (Exception exc) {
				_log.Error(exc, "Error while trying to delete remaining temp file: '{tempFile}'.",
					tempFile);
			}
		}
	}

	private void RemoveFile(string reason, string file) {
		_log.Debug(reason, file);
		File.SetAttributes(file, FileAttributes.Normal);
		File.Delete(file);
	}

	public ValueTask DisposeAsync() => Close(CancellationToken.None);

	public async ValueTask Close(CancellationToken token) {
		if (Interlocked.CompareExchange(ref _closed, 1, 0) != 0)
			return;

		bool chunksClosed = false;

		try {
			chunksClosed = await Manager.TryClose(token);
		} catch (Exception ex) {
			_log.Error(ex, "An error has occurred while closing the chunks.");
		}

		if (!chunksClosed)
			_log.Debug("One or more chunks are still open; skipping checkpoint flush.");

		Config.WriterCheckpoint.Close(flush: chunksClosed);
		Config.ChaserCheckpoint.Close(flush: chunksClosed);
		Config.EpochCheckpoint.Close(flush: chunksClosed);
		Config.TruncateCheckpoint.Close(flush: chunksClosed);
		Config.ProposalCheckpoint.Close(flush: chunksClosed);
		Config.StreamExistenceFilterCheckpoint.Close(flush: chunksClosed);
	}
}
