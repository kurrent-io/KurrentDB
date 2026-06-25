// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using static System.Globalization.CultureInfo;

namespace KurrentDB.KontrolPlane.StateMachine;

partial class ClusterStateMachine {
	private volatile SnapshotFile? _persistentSnapshot;

	ISnapshot? ISnapshotManager.Snapshot => _persistentSnapshot;

	ValueTask ISnapshotManager.ReclaimGarbageAsync(long watermark, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		try {
			ReclaimGarbage(watermark);
		} catch (Exception e) {
			task = ValueTask.FromException(e);
		}

		return task;
	}

	private void ReclaimGarbage(long watermark) {
		var snapshots = new List<FileInfo>();
		foreach (var snapshotFile in _location.EnumerateFiles()) {
			if (long.TryParse(snapshotFile.Name, out var snapshotIndex) && snapshotIndex < watermark) {
				snapshots.Add(snapshotFile);
			}
		}

		foreach (var snapshotFile in snapshots) {
			snapshotFile.Delete();
		}
	}

	private async ValueTask<long> InstallSnapshotAsync(LogEntry entry, CancellationToken token) {
		var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		var fs = new FileStream(fileName, new FileStreamOptions {
			Access = FileAccess.Write,
			Mode = FileMode.CreateNew,
			Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
			PreallocationSize = entry.Length.GetValueOrDefault(),
			Share = FileShare.None,
		});
		try {
			// save snapshot to the file
			await entry.WriteToAsync(fs, token: token);
			await fs.FlushAsync(token);

			InstallSnapshot(fileName);
		} finally {
			await fs.DisposeAsync();
			File.Delete(fileName);
		}

		return entry.Index;
	}

	private Snapshot InstallSnapshot(string fileName) {
		var newSnapshot = new Snapshot(_poolCapacity);
		newSnapshot.LoadFromFile(fileName);

		// swap current state
		Interlocked.Exchange(ref _snapshot, newSnapshot).Release();
		RefreshDatabaseTrackers(newSnapshot);
		return newSnapshot;
	}

	private Task SaveSnapshotAsync(Snapshot snapshot, CommandInfo info, CancellationToken token)
		=> Task.Run(() => SaveSnapshot(snapshot, info), token);

	private void SaveSnapshot(Snapshot snapshot, in CommandInfo info) {
		var snapshotFileName = Path.Combine(_location.FullName, info.Index.ToString(InvariantCulture));
		try {
			snapshot.SaveToFile(snapshotFileName);
		} catch when (File.Exists(snapshotFileName)) {
			File.Delete(snapshotFileName);
			throw;
		}

		_persistentSnapshot = new(snapshotFileName, info);
	}

	private sealed class SnapshotFile(string fileName, in CommandInfo info) : ISnapshot {
		private readonly FileInfo _file = new(fileName);
		private readonly CommandInfo _info = info;

		async ValueTask IDataTransferObject.WriteToAsync<TWriter>(TWriter writer, CancellationToken token) {
			await using var fs = _file.Open(new FileStreamOptions {
				Access = FileAccess.Read,
				Mode = FileMode.Open,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
			});

			await writer.CopyFromAsync(fs, token: token);
		}

		bool IDataTransferObject.IsReusable => true;

		long? IDataTransferObject.Length => _file.Length;

		long IRaftLogEntry.Term => _info.Term;

		long ISnapshot.Index => _info.Index;
	}
}
