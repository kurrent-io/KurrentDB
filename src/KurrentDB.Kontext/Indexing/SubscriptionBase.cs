// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing;

public abstract class SubscriptionBase : IDisposable {
	static readonly TimeSpan BatchIdleTimeout = TimeSpan.FromSeconds(10);

	const int DiskFlushThreshold = 10_000;
	const long MaxCheckpointLagBytes = 256 * 1024 * 1024;

	readonly ISystemClient _client;
	readonly IEventFilter _filter;
	readonly CheckpointIO _checkpoint;

	readonly WorkspaceFilter _workspaceFilter;

	protected readonly WorkspaceEntry Workspace;

	DateTime _lastStagedEventTimestamp = DateTime.MinValue;
	int _indexedSinceFlush;

	// An event's position moves through three stages: processed (staged or skipped), indexed
	// (its staged data handed to the stores), and checkpointed (persisted; never regresses).
	TFPos _lastProcessed;
	TFPos _lastIndexed;
	TFPos _lastCheckpointed;

	protected SubscriptionBase(
		ISystemClient client,
		WorkspaceEntry workspace,
		IEventFilter filter,
		string checkpointPath) {
		_client = client;
		_filter = filter;
		Workspace = workspace;
		_checkpoint = new CheckpointIO(checkpointPath);

		_workspaceFilter = WorkspaceFilter.Compile(workspace.FilterRules);
	}

	protected abstract string PipelineName { get; }
	protected abstract ILogger Logger { get; }
	protected abstract void OnCaughtUp();
	protected abstract TFPos FlushIndexes(TFPos current, bool disposing);
	protected abstract void UpdatePosition(ulong commitPosition);
	protected abstract void RecordIndexed(DateTime eventTimestamp);
	protected abstract KontextProgressTracker.Scope StartIndexScope();
	protected abstract KontextProgressTracker.Scope StartCommitScope();

	protected abstract int BatchCount { get; }
	protected abstract bool IsBatchFull { get; }

	protected abstract ValueTask StageEventAsync(
		ResolvedEvent evt, IndexKind targetKind, CancellationToken ct);

	protected abstract Task IndexBatchAsync(CancellationToken ct);

	public async Task RunAsync(CancellationToken stoppingToken) {
		const int retryDelaySec = 5;

		try {
			while (!stoppingToken.IsCancellationRequested) {
				try {
					await RunSubscription(stoppingToken);
					return;
				} catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
					return;
				} catch (Exception ex) {
					Logger.LogWarning(ex, "{Pipeline}[{Workspace}] subscription interrupted, retrying in {Delay}s...",
						PipelineName, Workspace.Name, retryDelaySec);
					await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), stoppingToken);
				}
			}
		} finally {
			FlushToDisk(disposing: true);
			_checkpoint.Dispose();
		}
	}

	async Task RunSubscription(CancellationToken ct) {
		var lastPosition = _checkpoint.Load();
		if (lastPosition.HasValue) {
			_lastProcessed = _lastIndexed = _lastCheckpointed =
				new TFPos((long)lastPosition.Value.Commit, (long)lastPosition.Value.Prepare);
			Logger.LogInformation("{Pipeline}[{Workspace}]: resuming from position {Commit}/{Prepare}",
				PipelineName, Workspace.Name, lastPosition.Value.Commit, lastPosition.Value.Prepare);
		} else {
			Logger.LogInformation("{Pipeline}[{Workspace}]: starting from the beginning",
				PipelineName, Workspace.Name);
		}

		var enumerator = _client.SubscribeToAll(lastPosition, _filter, ct).GetAsyncEnumerator(ct);
		Task<bool>? pendingMove = null;
		var isLive = false;

		try {
			while (true) {
				pendingMove ??= enumerator.MoveNextAsync().AsTask();

				bool hasNext;
				try {
					hasNext = await pendingMove.WaitAsync(BatchIdleTimeout, ct);
				} catch (TimeoutException) {
					await IndexPendingAsync(ct);
					if (isLive)
						OnCaughtUp();
					continue;
				}
				pendingMove = null;

				if (!hasNext)
					break;
				var current = enumerator.Current;

				if (current.CaughtUp) {
					await IndexPendingAsync(ct);
					OnCaughtUp();
					isLive = true;
					continue;
				}

				if (current.Checkpoint is { } cp) {
					TrackCheckpoint(new TFPos((long)cp.Commit, (long)cp.Prepare));
					continue;
				}

				await ProcessEventAsync(current.Event!.Value, ct);
			}

			await IndexPendingAsync(ct);
		} finally {
			// Let any in-flight MoveNextAsync settle before disposing — disposing the iterator mid-move throws.
			if (pendingMove is not null) {
				try {
					await pendingMove;
				} catch (Exception) {
					// Faults with the cancellation/error we're already unwinding.
				}
			}
			await enumerator.DisposeAsync();
		}
	}

	async Task ProcessEventAsync(ResolvedEvent evt, CancellationToken ct) {
		var record = evt.OriginalEvent;
		var position = evt.OriginalPosition!.Value;

		// The server-side filter has already narrowed events down to our memory stream
		// plus our prefix rules. The only client-side routing decision is memory vs events
		var targetKind = record.EventStreamId.StartsWith(Workspace.MemoryStreamPrefix, StringComparison.Ordinal)
			? IndexKind.Memory
			: IndexKind.Events;

		// Skip events on system streams or with system event types. Memory events
		// route to IndexKind.Memory above and bypass this check.
		if (targetKind == IndexKind.Events
			&& (record.EventStreamId.StartsWith('$') || record.EventType.StartsWith('$'))) {
			TrackCheckpoint(position);
			return;
		}

		// Skip non-memory events that don't pass the workspace's filter rules
		if (targetKind == IndexKind.Events && !MatchesFilterRules(evt)) {
			TrackCheckpoint(position);
			return;
		}

		_lastStagedEventTimestamp = record.TimeStamp;
		await StageEventAsync(evt, targetKind, ct);
		TrackCheckpoint(position);

		if (IsBatchFull)
			await IndexPendingAsync(ct);
	}

	async Task IndexPendingAsync(CancellationToken ct) {
		if (BatchCount > 0) {
			var count = BatchCount;
			var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

			using (StartIndexScope())
				await IndexBatchAsync(ct);
			_indexedSinceFlush += count;
			_lastIndexed = _lastProcessed;

			var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);
			Logger.LogTrace("{Pipeline}[{Workspace}] indexed {Count} events in {Time:F0}ms",
				PipelineName, Workspace.Name, count, elapsed.TotalMilliseconds);
		}

		if (_indexedSinceFlush >= DiskFlushThreshold)
			FlushToDisk();
	}

	void FlushToDisk(bool disposing = false) {
		var current = LastFullyIndexedPosition();

		TFPos watermark;
		using (StartCommitScope())
			watermark = FlushIndexes(current, disposing);

		RecordFlushed();
		SaveCheckpoint(watermark);
	}

	TFPos LastFullyIndexedPosition() =>
		BatchCount > 0 ? _lastIndexed : _lastProcessed;

	void SaveCheckpoint(TFPos watermark) {
		// Store watermarks start at (0,0) after a resume; never regress the checkpoint
		if (watermark < _lastCheckpointed)
			watermark = _lastCheckpointed;

		_checkpoint.Save((ulong)watermark.CommitPosition, (ulong)watermark.PreparePosition);
		_lastCheckpointed = watermark;
	}

	void RecordFlushed() {
		if (_indexedSinceFlush == 0)
			return;

		if (_lastStagedEventTimestamp != DateTime.MinValue)
			RecordIndexed(_lastStagedEventTimestamp);
		_indexedSinceFlush = 0;
	}

	protected static TFPos FlushStore(IIndexStore store, TFPos current) {
		var watermark = store.Flush(current, force: false);
		if (current.CommitPosition - watermark.CommitPosition > MaxCheckpointLagBytes)
			watermark = store.Flush(current, force: true);
		return watermark;
	}

	protected static TFPos Min(TFPos a, TFPos b) => a < b ? a : b;

	void TrackCheckpoint(TFPos position) {
		_lastProcessed = position;
		UpdatePosition((ulong)position.CommitPosition);
	}

	internal bool MatchesFilterRules(ResolvedEvent evt) {
		var record = evt.OriginalEvent;
		return _workspaceFilter.Evaluate(record, Logger) switch {
			FilterResult.Match => true,
			FilterResult.Excluded => false,
			// The server-side subscription filter should only deliver prefix-matching events,
			// so a no-match here means a configuration/subscription bug.
			_ => throw new InvalidOperationException(
				$"[{Workspace.Name}] no filter rule matched stream '{record.EventStreamId}' that " +
				"the server-side filter let through."),
		};
	}

	public virtual void Dispose() => _checkpoint.Dispose();
}
