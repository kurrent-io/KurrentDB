// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DotNext;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using DotNext.Threading;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

using Queries;

/// <summary>
/// Represents internal Kontrol Plane database.
/// </summary>
internal sealed partial class ClusterStateMachine : Disposable, IStateMachine, IAsyncDisposable {
	private readonly DirectoryInfo _location;
	private readonly int _poolCapacity;
	private readonly ConcurrentDictionary<string, AsyncStateTracker> _databases;
	private volatile ClusterState _state;
	private volatile Task? _snapshotTask;

	public ClusterStateMachine(DirectoryInfo location, int connectionPoolCapacity) {
		if (!location.Exists)
			location.Create();

		_location = location;
		_state = new(connectionPoolCapacity);
		_databases = new();
		_poolCapacity = connectionPoolCapacity;
	}

	/// <summary>
	/// Recovers the internal state from the last known persisted snapshot.
	/// </summary>
	public void Recover() {
		// Attempt to open the latest persisted snapshot
		var snapshots = new SortedDictionary<long, FileInfo>();
		foreach (var snapshotFile in _location.EnumerateFiles()) {
			if (long.TryParse(snapshotFile.Name, out var snapshotIndex)) {
				snapshots[snapshotIndex] = snapshotFile;
			}
		}

		if (snapshots.Count > 0) {
			var latestSnapshotFile = snapshots.MaxBy(static pair => pair.Key).Value;

			var newSnapshot = InstallSnapshot(latestSnapshotFile.FullName);
			_persistentSnapshot = new(latestSnapshotFile.FullName, newSnapshot.LastAppliedCommand);
		} else {
			_state.Initialize();
			_databases[Database.MainDatabaseId] = new();
		}

		snapshots.Clear(); // help GC
	}

	private void RefreshDatabaseTrackers(ClusterState clusterState) {
		var loadedTrackers = new HashSet<string>();
		using (clusterState.RentConnection(out var connection)) {
			foreach (var databaseId in connection.GetDatabases()) {
				loadedTrackers.Add(databaseId);
			}
		}

		// add missing trackers
		foreach (var databaseId in loadedTrackers) {
			if (!_databases.ContainsKey(databaseId)) {
				var tracker = new AsyncStateTracker();
				if (!_databases.TryAdd(databaseId, tracker)) {
					tracker.TryComplete();
				}
			}
		}

		// remove deleted trackers
		foreach (var databaseId in _databases.Keys
			         .Where(databaseId => !loadedTrackers.Contains(databaseId))
			         .ToHashSet()) {
			if (_databases.TryRemove(databaseId, out var tracker)) {
				tracker.TryComplete();
			}
		}

		loadedTrackers.Clear(); // help GC
	}

	/// <summary>
	/// Gets or sets the snapshot depth.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to zero.</exception>
	public int SnapshotDepth {
		get;
		init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
	} = 100;

	/// <summary>
	/// Tracks database changes as a stream of state snapshots.
	/// </summary>
	/// <remarks>
	/// The caller must release the snapshot with <see cref="ClusterState.Release()"/> method.
	/// </remarks>
	/// <param name="databaseId">The identifier of the database.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>A stream over cluster state snapshots.</returns>
	public async IAsyncEnumerable<ClusterState> TrackChangesAsync(string databaseId,
		[EnumeratorCancellation] CancellationToken token) {
		for (AsyncStateTracker.Token currentState;; token.ThrowIfCancellationRequested()) {
			if (!_databases.TryGetValue(databaseId, out var tracker) || IsDisposingOrDisposed)
				break;

			currentState = tracker.CurrentState;
			var snapshotCopy = _state;

			// The current snapshot cannot be acquired, which means that it's no longer available. Retry the operation
			// and do Yield() to increase a chance to get latest snapshot copy from '_snapshot' field
			if (!snapshotCopy.TryAcquire()) {
				await Task.Yield();
				continue;
			}

			yield return snapshotCopy;
			if (!await tracker.WaitNextAsync(currentState, token))
				break;
		}
	}

	/// <summary>
	/// Captures the current database state asynchronously.
	/// </summary>
	/// <remarks>
	/// The caller must release the snapshot with <see cref="ClusterState.Release()"/> method.
	/// </remarks>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The acquired snapshot.</returns>
	public async ValueTask<ClusterState> CaptureCurrentStateAsync(CancellationToken token) {
		ClusterState clusterStateCopy;
		for (;; token.ThrowIfCancellationRequested()) {
			clusterStateCopy = _state;
			if (clusterStateCopy.TryAcquire())
				break;

			// The current snapshot cannot be acquired, which means that it's no longer available. Retry the operation
			// and do Yield() to increase a chance to get latest snapshot copy from '_snapshot' field
			await Task.Yield();
		}

		return clusterStateCopy;
	}

	ValueTask<long> IStateMachine.ApplyAsync(LogEntry entry, CancellationToken token) {
		var lastAppliedIndex = _state.LastAppliedCommand.Index;
		if (entry.Index <= lastAppliedIndex)
			return ValueTask.FromResult(lastAppliedIndex);

		return entry.IsSnapshot
			? InstallSnapshotAsync(entry, token)
			: ApplyAsync(entry, token);
	}

	private void CompleteTrackers() {
		foreach (var tracker in _databases.Values) {
			tracker.TryComplete();
		}
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_state.Release();
			CompleteTrackers();
			_databases.Clear();
		}

		base.Dispose(disposing);
	}

	protected override async ValueTask DisposeAsyncCore() {
		try {
			await (_snapshotTask ?? Task.CompletedTask)
				.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		} finally {
			Dispose(disposing: true);
		}
	}

	public new ValueTask DisposeAsync() => base.DisposeAsync();
}
