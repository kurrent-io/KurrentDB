// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using DotNext.Threading;

namespace KurrentDB.KontrolPlane.StateMachine;

/// <summary>
/// Represents internal Kontrol Plane database.
/// </summary>
/// <param name="location"></param>
internal sealed partial class ClusterStateMachine(DirectoryInfo location) : SimpleStateMachine(location) {
	private readonly AsyncTrigger _stateChanged = new();
	private ClusterState _state = new();

	public ClusterState CurrentState {
		get => Volatile.Read(in _state);
		private set => TrySetCurrentState(value);
	}

	private bool TrySetCurrentState(ClusterState newState) {
		if (ReferenceEquals(Volatile.Read(in _state), newState))
			return false;

		_state = newState;
		_stateChanged.Signal(resumeAll: true); // acts as a barrier
		return true;
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
	/// Tracks cluster state changes as a stream of state snapshots.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>A stream over cluster state snapshots.</returns>
	public async IAsyncEnumerable<IReadOnlyDictionary<string, IDatabase>> TrackChangesAsync(
		[EnumeratorCancellation] CancellationToken token) {
		do {
			var actual = CurrentState;
			yield return actual;
			await _stateChanged.SpinWaitAsync(new DatabaseChangeTracker(this) { ExpectedVersion = actual.Version }, token);
		} while (!token.IsCancellationRequested);
	}

	// Restore state from the snapshot
	protected override ValueTask RestoreAsync(FileInfo snapshotFile, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		var fs = default(FileStream);
		try {
			fs = snapshotFile.Open(new FileStreamOptions {
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.Read,
				BufferSize = 4096,
				Options = FileOptions.SequentialScan
			});

			_state = ClusterState.Parser.ParseFrom(fs);
			_stateChanged.Signal(resumeAll: true);
		} catch (Exception e) {
			task = ValueTask.FromException(e);
		} finally {
			fs?.Dispose();
		}

		return task;
	}

	// Persist current state
	protected override ValueTask PersistAsync(IAsyncBinaryWriter writer, CancellationToken token)
		=> _state.WriteToAsync(writer, token);

	// Apply log entry from the WAL to the current state
	protected override async ValueTask<bool> ApplyAsync(LogEntry entry, CancellationToken token) {
		switch (entry.CommandId) {
			case LogEntries.AddOrUpdateDatabase.TypeId:
				var result = AddDatabase(await DeserializeAsync<LogEntries.AddOrUpdateDatabase>(entry, token));
				(entry.Context as TaskCompletionSource<bool>)?.TrySetResult(result);
				break;
			case LogEntries.RemoveDatabase.TypeId:
				result = RemoveDatabase(await DeserializeAsync<LogEntries.RemoveDatabase>(entry, token));
				(entry.Context as TaskCompletionSource<bool>)?.TrySetResult(result);
				break;
			case LogEntries.AddOrUpdateDatabaseNode.TypeId:
				result = AddDatabaseNode(await DeserializeAsync<LogEntries.AddOrUpdateDatabaseNode>(entry, token));
				(entry.Context as TaskCompletionSource<bool>)?.TrySetResult(result);
				break;
			case LogEntries.RemoveDatabaseNode.TypeId:
				result = RemoveDatabaseNode(await DeserializeAsync<LogEntries.RemoveDatabaseNode>(entry, token));
				(entry.Context as TaskCompletionSource<bool>)?.TrySetResult(result);
				break;
			case LogEntries.AppointLeader.TypeId:
				result = AppointLeader(await DeserializeAsync<LogEntries.AppointLeader>(entry, token));
				break;
			default:
				Debug.Fail($"Unexpected entry type {entry.CommandId}");
				break;
		}

		return entry.Index % SnapshotDepth is 0;
	}

	private static ValueTask<T> DeserializeAsync<T>(in LogEntry entry, CancellationToken token)
		where T : class, IProtobufSerializable<T> {
		ValueTask<T> task;
		if (entry.TryGetPayload(out var sequence)) {
			// fast path, deserialize from memory
			try {
				task = new(T.Parser.ParseFrom(sequence));
			} catch (Exception e) {
				task = ValueTask.FromException<T>(e);
			}
		} else {
			task = DeserializeSlowAsync(entry, token);
		}

		return task;

		static async ValueTask<T> DeserializeSlowAsync(LogEntry entry, CancellationToken token) {
			using var owner = await entry.ToMemoryAsync(token: token);
			return T.Parser.ParseFrom(owner.Memory.Span);
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct DatabaseChangeTracker(ClusterStateMachine stateMachine) : ISupplier<bool> {
		private ulong ActualVersion => stateMachine.CurrentState.Version;

		public required ulong ExpectedVersion { get; init; }

		bool ISupplier<bool>.Invoke() => ActualVersion == ExpectedVersion;
	}
}
