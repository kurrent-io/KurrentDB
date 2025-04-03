// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.Services.Archive.Storage.Exceptions;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Serilog;

namespace EventStore.Core.Services.Archive.ArchiveCatchup;

// The archive catchup process downloads chunks that are missing locally from the archive.
//
// This is needed in some cases:
// i)  a follower may be far behind the leader and the latter may have already deleted archived chunks locally.
// ii) under normal circumstances, a leader should never need to catch up from the archive. however, if a cluster's
//     data was restored from a backup, we can end up in a situation where the leader-to-be is behind the archive.
//     in this case, we still want all nodes to catch up with the archive *before* joining the cluster to maintain
//     consistency between the data that's in the cluster and in the archive.

public class ArchiveCatchup : IClusterVNodeStartupTask {
	private readonly string _dbPath;
	private readonly ICheckpoint _writerCheckpoint;
	private readonly ICheckpoint _chaserCheckpoint;
	private readonly ICheckpoint _epochCheckpoint;
	private readonly int _chunkSize;
	private readonly IArchiveStorageReader _archiveReader;
	private readonly IArchiveNamingStrategy _namingStrategy;

	private static readonly ILogger Log = Serilog.Log.ForContext<ArchiveCatchup>();

	public ArchiveCatchup(
		string dbPath,
		ICheckpoint writerCheckpoint,
		ICheckpoint chaserCheckpoint,
		ICheckpoint epochCheckpoint,
		int chunkSize,
		IArchiveStorageReader archiveStorageReader,
		IArchiveNamingStrategy namingStrategy) {
		_dbPath = dbPath;
		_writerCheckpoint = writerCheckpoint;
		_chaserCheckpoint = chaserCheckpoint;
		_epochCheckpoint = epochCheckpoint;
		_chunkSize = chunkSize;
		_archiveReader = archiveStorageReader;
		_namingStrategy = namingStrategy;
	}

	public async ValueTask Run(CancellationToken ct) {
		var writerChk = _writerCheckpoint.Read();
		var archiveChk = await _archiveReader.GetCheckpoint(ct);

		if (writerChk >= archiveChk)
			return;

		Log.Information("Catching up with the archive. Writer checkpoint: 0x{writerCheckpoint:X}, Archive checkpoint: 0x{archiveCheckpoint:X}.",
			writerChk, archiveChk);

		if (writerChk == 0)
			Log.Warning(
				"This node has no local data and will download the entire archive. " +
				"It is preferable to restore the node from a backup instead, so that it will download only the most recent data. " +
				"A new backup can be created from one of the other nodes if necessary.");

		while (!await CatchUpWithArchive(writerChk, archiveChk, ct))
			writerChk = _writerCheckpoint.Read();
	}

	// returns true if the catchup is done
	// returns false if it needs to be invoked again to continue the catchup
	private async Task<bool> CatchUpWithArchive(long writerChk, long archiveChk, CancellationToken ct) {
		var logicalChunkStartNumber = (int) (writerChk / _chunkSize);
		var logicalChunkEndNumber = (int) (archiveChk / _chunkSize);

		for (var logicalChunkNumber = logicalChunkStartNumber; logicalChunkNumber < logicalChunkEndNumber; logicalChunkNumber++)
			if (!await FetchAndCommitChunk(logicalChunkNumber, ct))
				return false;

		Log.Information("Catch-up with the archive completed");
		return true;
	}

	private async Task<bool> FetchAndCommitChunk(int logicalChunkNumber, CancellationToken ct) {
		var destinationFile = _namingStrategy.GetBlobNameFor(logicalChunkNumber);
		var destinationPath = Path.Combine(_dbPath, destinationFile);
		if (!await FetchChunk(logicalChunkNumber, destinationPath, ct))
			return false;

		await CommitChunk(destinationPath, ct);
		return true;
	}

	private async Task<bool> FetchChunk(int logicalChunkNumber, string destinationPath, CancellationToken ct) {
		try {
			Log.Information("Fetching chunk: {logicalChunkNumber} from the archive", logicalChunkNumber);

			var tempPath = Path.Combine(_dbPath, Guid.NewGuid() + ".archive.tmp");

			using var handle = await _archiveReader.OpenForReadAsync(logicalChunkNumber, ct);
			await using (var inputStream = handle.CreateStream()) {
				await using var outputStream = File.Open(
					path: tempPath,
					options: new FileStreamOptions {
						Mode = FileMode.CreateNew,
						Access = FileAccess.ReadWrite,
						Share = FileShare.None,
						Options = FileOptions.Asynchronous,
						PreallocationSize = _chunkSize,
					});

				await inputStream.CopyToAsync(outputStream, ct);
			}

			if (File.Exists(destinationPath)) {
				var backupPath = $"{destinationPath}.archive.bkup";
				Log.Information("Backing up {chunk} to {chunkBackup}", Path.GetFileName(destinationPath), Path.GetFileName(backupPath));
				File.Move(destinationPath, backupPath, overwrite: true);
			}

			File.Move(tempPath, destinationPath);

			return true;
		} catch (ChunkDeletedException) {
			Log.Warning("Failed to fetch chunk: {logicalChunkNumber} from the archive as it was deleted. This can happen if the archive is being scavenged.", logicalChunkNumber);
			return false;
		}
	}

	private async Task CommitChunk(string chunkPath, CancellationToken ct) {
		await using var headerStream = File.OpenRead(chunkPath);
		var header = await ChunkHeader.FromStream(headerStream, ct);

		_epochCheckpoint.Write(-1);
		_epochCheckpoint.Flush();
		Log.Debug("Reset {checkpoint} checkpoint to: 0x{position:X}", _epochCheckpoint.Name, -1);

		_chaserCheckpoint.Write(header.ChunkEndPosition);
		_chaserCheckpoint.Flush();
		Log.Debug("Moved {checkpoint} checkpoint forward to: 0x{position:X}", _chaserCheckpoint.Name, header.ChunkEndPosition);

		_writerCheckpoint.Write(header.ChunkEndPosition);
		_writerCheckpoint.Flush();
		Log.Debug("Moved {checkpoint} checkpoint forward to: 0x{position:X}", _writerCheckpoint.Name, header.ChunkEndPosition);
	}
}
