// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using static System.Globalization.CultureInfo;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

partial class ClusterStateMachine {
	private readonly nint openFileFunction;
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
		var tempFile = CreateTempFileName();
		await using (var tempSnapshot = new FileStream(tempFile, new FileStreamOptions {
			             Access = FileAccess.Write,
			             Mode = FileMode.CreateNew,
			             Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
			             PreallocationSize = entry.Length.GetValueOrDefault(),
			             Share = FileShare.None,
		             })) {
			// save snapshot to the file
			await entry.WriteToAsync(tempSnapshot, token: token);
			await tempSnapshot.FlushAsync(token);
		}

		InstallSnapshot(tempFile, in LoadSnapshot(tempFile).LastAppliedCommand);
		return entry.Index;
	}

	private ClusterState LoadSnapshot(string fileName) {
		var newSnapshot = new ClusterState(_poolCapacity);
		newSnapshot.LoadFromFile(fileName);

		// swap current state
		Interlocked.Exchange(ref _state, newSnapshot).Release();
		RefreshDatabaseTrackers(newSnapshot);
		return newSnapshot;
	}

	private Task SaveSnapshotAsync(ClusterState clusterState, CommandInfo info, CancellationToken token) {
		Task task;
		if (clusterState.TryAcquire()) {
			task = Task.Run(() => SaveSnapshot(clusterState, info), token);
			task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(clusterState.Release);
		} else {
			task = Task.CompletedTask;
		}

		return task;
	}

	private void SaveSnapshot(ClusterState clusterState, in CommandInfo info) {

		// Temp file needs to be on the same file system
		var tempFileName = CreateTempFileName();
		clusterState.SaveToFile(tempFileName);

		InstallSnapshot(tempFileName, in info);
	}

	private unsafe void Move(string sourceFileName, string destFileName) {
		// This operation is atomic on modern file systems
		File.Move(sourceFileName, destFileName, overwrite: true);

		if (OperatingSystem.IsLinux() && openFileFunction is not 0) {
			FlushToDisk(Path.GetDirectoryName(destFileName),
				(delegate*unmanaged<byte*, int, int, int>)openFileFunction);
		}
	}

	private string CreateTempFileName()
		=> Path.Combine(_location.FullName, string.Concat(Path.GetRandomFileName(), ".tmp"));

	private void InstallSnapshot(string tempSnapshot, in CommandInfo info) {
		var snapshotFileName = Path.Combine(_location.FullName, info.Index.ToString(InvariantCulture));
		Move(tempSnapshot, snapshotFileName);
		_persistentSnapshot = new(snapshotFileName, in info);
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
