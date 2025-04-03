// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using EventStore.Common.Utils;

namespace EventStore.Core.TransactionLog.Scavenging;

public static class ParallelLoop {
	private static readonly Task<int> _neverComplete;

	static ParallelLoop() {
		var tcs = new TaskCompletionSource<int>();
		_neverComplete = tcs.Task;
	}

	// passes each item in `source` to `process`, according to the `degreeOfParallelism`.
	// process is guaranteed to be called asynchronously.
	// calls to emitCheckpoint are serialized (one at a time) and passed the latest checkpoint that is complete
	// items are queried for checkpoint
	//   - getCheckpointInclusive returns the checkpoint that can be emitted when all items up to
	//     and including this one have been completed.
	//   - getCheckpointExclusive returns the checkpoint that can be emitted when all items up to
	//     but excluding this one have been completed.
	public static async ValueTask RunWithTrailingCheckpointAsync<T>(
		IAsyncEnumerable<T> source,
		int degreeOfParallelism,
		Func<T, int> getCheckpointInclusive,
		Func<T, int?> getCheckpointExclusive,
		Func<int, T, CancellationToken, Task> process,
		Action<int> emitCheckpoint,
		Action onConsiderEmit = null,
		CancellationToken token = default) {

		Ensure.Positive(degreeOfParallelism, nameof(degreeOfParallelism));

		// in each slot we store the checkpoint that can be emitted when every item before the one
		// being processed in that slot is completed.
		// null means it cannot emit a checkpoint at all.
		// int.max means it places no limit on the checkpoint.
		var checkpoints = new int?[degreeOfParallelism];
		var tasksInProgress = new Task<int>[degreeOfParallelism];

		for (var i = 0; i < degreeOfParallelism; i++) {
			tasksInProgress[i] = _neverComplete;
			checkpoints[i] = int.MaxValue;
		}

		// checkpoint of the last element, to emit at the end.
		var endCheckpoint = default(int?);
		var lastEmittedCheckpoint = default(int?);

		void PrepareProcessingItem(int slot, T item) {
			checkpoints[slot] = getCheckpointExclusive(item);
		}

		[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder<>))]
		static async Task<int> SpawnProcess(Func<int, T, CancellationToken, Task> process, int slot, T item,
			CancellationToken token) {
			await process.Invoke(slot, item, token);
			return slot;
		}

		void EmitCheckpoint() {
			onConsiderEmit?.Invoke();

			// find the the minimum checkpoint, we can emit it.
			if (checkpoints.Any(static x => x is null))
				return;

			var checkpointToEmit = checkpoints.Min().Value;

			if (lastEmittedCheckpoint != null && checkpointToEmit <= lastEmittedCheckpoint)
				return;

			if (checkpointToEmit is int.MaxValue)
				checkpointToEmit = endCheckpoint.Value;

			emitCheckpoint(checkpointToEmit);
			lastEmittedCheckpoint = checkpointToEmit;
		}

		// process the source
		var slotsInUse = 0;
		await foreach (var item in source.WithCancellation(token)) {
			endCheckpoint = getCheckpointInclusive(item);
			if (slotsInUse < tasksInProgress.Length) {
				PrepareProcessingItem(slotsInUse, item);
				tasksInProgress[slotsInUse] = SpawnProcess(process, slotsInUse, item, token);
				slotsInUse++;
			} else {
				var task = await Task.WhenAny(tasksInProgress);
				var slot = await task;
				PrepareProcessingItem(slot, item);
				EmitCheckpoint();
				tasksInProgress[slot] = SpawnProcess(process, slot, item, token);
			}
		}

		// drain the tasks
		while (slotsInUse > 0) {
			var task = await Task.WhenAny(tasksInProgress);
			var slot = await task;
			checkpoints[slot] = int.MaxValue;
			EmitCheckpoint();
			tasksInProgress[slot] = _neverComplete;
			slotsInUse--;
		}
	}
}
