// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing.USearch;

sealed class SegmentMerger(
	Segments segments,
	string directory,
	ILogger logger,
	string name) : IDisposable {

	readonly object _lock = new();
	readonly CancellationTokenSource _cts = new();
	bool _running;
	Task? _task;

	public void Trigger() {
		lock (_lock) {
			if (_running || _cts.IsCancellationRequested)
				return;
			_running = true;
			_task = Task.Run(() => Run(_cts.Token));
		}
	}

	void Run(CancellationToken ct) {
		try {
			while (!ct.IsCancellationRequested && segments.FindMergeGroup() is { } work) {
				var (group, targetLevel) = work;
				var merged = Segment.Merge(directory, group, targetLevel, ct);
				segments.Replace(group, merged);

				logger.LogDebug("Merged {Count} USearch segments into level {Level} ({Size} vectors) for {Index}",
					group.Count, targetLevel, merged.Size, name);
			}
		} catch (OperationCanceledException) {
			// disposing — fine to abandon a merge midway; its partial output is never referenced.
		} catch (Exception ex) {
			logger.LogError(ex, "USearch segment merge failed for {Index}", name);
		} finally {
			lock (_lock)
				_running = false;
		}

		// A seal may have queued more work while we were finishing; re-arm if so.
		if (!ct.IsCancellationRequested && segments.FindMergeGroup() is not null)
			Trigger();
	}

	public void Dispose() {
		_cts.Cancel();

		Task? task;
		lock (_lock)
			task = _task;
		try {
			task?.Wait(TimeSpan.FromSeconds(30));
		} catch (AggregateException) {
			// merge faulted during shutdown — already logged in Run
		}

		_cts.Dispose();
	}
}
