// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;

namespace KurrentDB.Projections.Core.Services.Processing;

/// <summary>
/// Staged processing queue allows queued processing of multi-step tasks.  The
/// processing order allows multiple tasks to be processed at the same time with a constraint
/// a) ordered stage: all preceding tasks in the queue has already started processing at the given stage.
/// b) unordered stage: no items with the same correlation_id are in the queue before current item
///
/// For instance:  multiple foreach sub-projections can request state to be loaded, then they can process
/// it and store.  But no subprojection can process events prior to preceding projections has completed processing.
/// </summary>
public class StagedProcessingQueue {
	private class TaskEntry(StagedTask task, long sequence) {
		public readonly StagedTask Task = task;
		public readonly long Sequence = sequence;
		public bool Busy;
		public object BusyCorrelationId;
		public TaskEntry PreviousByCorrelation;
		public TaskEntry NextByCorrelation;
		public TaskEntry Next;
		public bool Completed;
		public int ReadForStage = -1;

		public override string ToString() => $"ReadForStage: {ReadForStage},  Busy: {Busy}, Completed: {Completed} => {Task}";
	}

	private class StageEntry {
		public TaskEntry Entry;
		public StageEntry Next;
	}

	private readonly bool[] _orderedStage;
	private readonly Dictionary<object, TaskEntry> _correlationLastEntries = new();
	private StageEntry[] _byUnorderedStageFirst; // null means all processed? so append need to set it?
	private StageEntry[] _byUnorderedStageLast; // null means all processed? so append need to set it?
	private TaskEntry[] _byOrderedStageLast; // null means all processed? so append need to set it?
	private long _sequence;
	private TaskEntry _first;
	private TaskEntry _last;
	private readonly int _maxStage;
	public event Action EnsureTickPending;

	public StagedProcessingQueue(bool[] orderedStage) {
		_orderedStage = orderedStage.ToArray();
		_byUnorderedStageFirst = new StageEntry[_orderedStage.Length];
		_byUnorderedStageLast = new StageEntry[_orderedStage.Length];
		_byOrderedStageLast = new TaskEntry[_orderedStage.Length];
		_maxStage = _orderedStage.Length - 1;
	}

	public int Count { get; private set; }

	public void Enqueue(StagedTask stagedTask) {
		var entry = new TaskEntry(stagedTask, ++_sequence);
		if (_first == null) {
			_first = entry;
			_last = entry;
			Count = 1;
		} else {
			_last.Next = entry;
			_last = entry;
			Count++;
		}

		// re-initialize already completed queues
		for (var stage = 0; stage <= _maxStage; stage++)
			if (_orderedStage[stage] && _byOrderedStageLast[stage] == null)
				_byOrderedStageLast[stage] = entry;

		SetEntryCorrelation(entry, stagedTask.InitialCorrelationId);
		EnqueueForStage(entry, 0);
	}

	public bool Process(int max = 1) {
		int processed = 0;
		int fromStage = _maxStage;
		while (Count > 0 && processed < max) {
			RemoveCompleted();
			var entry = GetEntryToProcess(fromStage);
			if (entry == null)
				break;
			ProcessEntry(entry);
			fromStage = entry.ReadForStage;
			processed++;
		}

		return processed > 0;
	}

	private void ProcessEntry(TaskEntry entry) {
		// here we should be at the first StagedTask of current processing level which is not busy
		entry.Busy = true;
		AdvanceStage(entry.ReadForStage, entry);
		entry.Task.Process(
			entry.ReadForStage,
			(readyForStage, newCorrelationId) => CompleteTaskProcessing(entry, readyForStage, newCorrelationId));
	}

	private TaskEntry GetEntryToProcess(int fromStage) {
		var stageIndex = fromStage;
		while (stageIndex >= 0) {
			TaskEntry task = null;
			if (!_orderedStage[stageIndex]) {
				if (_byUnorderedStageFirst[stageIndex] != null
					&& _byUnorderedStageFirst[stageIndex].Entry.PreviousByCorrelation == null) {
					var stageEntry = _byUnorderedStageFirst[stageIndex];
					task = stageEntry.Entry;
				}
			} else {
				var taskEntry = _byOrderedStageLast[stageIndex];
				if (taskEntry != null && taskEntry.ReadForStage == stageIndex && !taskEntry.Busy
					&& !taskEntry.Completed && taskEntry.PreviousByCorrelation == null)
					task = taskEntry;
			}

			if (task == null) {
				stageIndex--;
				continue;
			}

			return task.ReadForStage != stageIndex ? throw new Exception() : task;
		}

		return null;
	}

	private void RemoveCompleted() {
		while (_first is { Completed: true }) {
			var task = _first;
			_first = task.Next;
			if (_first == null)
				_last = null;
			Count--;
			if (task.BusyCorrelationId != null) {
				var nextByCorrelation = task.NextByCorrelation;
				if (nextByCorrelation != null) {
					if (nextByCorrelation.PreviousByCorrelation != task)
						throw new Exception("Invalid linked list by correlation");
					task.NextByCorrelation = null;
					nextByCorrelation.PreviousByCorrelation = null;
					if (!_orderedStage[nextByCorrelation.ReadForStage])
						EnqueueForStage(nextByCorrelation, nextByCorrelation.ReadForStage);
				} else {
					// remove the last one
					_correlationLastEntries.Remove(task.BusyCorrelationId);
				}
			}
		}
	}

	private void CompleteTaskProcessing(TaskEntry entry, int readyForStage, object newCorrelationId) {
		if (!entry.Busy)
			throw new InvalidOperationException("Task was not in progress");
		entry.Busy = false;
		SetEntryCorrelation(entry, newCorrelationId);
		if (readyForStage < 0) {
			MarkCompletedTask(entry);
			if (entry == _first)
				RemoveCompleted();
		} else
			EnqueueForStage(entry, readyForStage);

		EnsureTickPending?.Invoke();
	}

	private void EnqueueForStage(TaskEntry entry, int readyForStage) {
		entry.ReadForStage = readyForStage;
		if (!_orderedStage[readyForStage] && (entry.PreviousByCorrelation == null)) {
			var stageEntry = new StageEntry { Entry = entry, Next = null };
			if (_byUnorderedStageFirst[readyForStage] != null) {
				_byUnorderedStageLast[readyForStage].Next = stageEntry;
			} else {
				_byUnorderedStageFirst[readyForStage] = stageEntry;
			}

			_byUnorderedStageLast[readyForStage] = stageEntry;
		}
	}

	private void AdvanceStage(int stage, TaskEntry entry) {
		if (!_orderedStage[stage]) {
			if (_byUnorderedStageFirst[stage].Entry != entry)
				throw new ArgumentException($"entry is not a head of the queue at the stage {stage}", nameof(entry));
			_byUnorderedStageFirst[stage] = _byUnorderedStageFirst[stage].Next;
			if (_byUnorderedStageFirst[stage] == null)
				_byUnorderedStageLast[stage] = null;
		} else {
			if (_byOrderedStageLast[stage] != entry)
				throw new ArgumentException($"entry is not a head of the queue at the stage {stage}", nameof(entry));
			_byOrderedStageLast[stage] = entry.Next;
		}
	}

	private void SetEntryCorrelation(TaskEntry entry, object newCorrelationId) {
		if (!Equals(entry.BusyCorrelationId, newCorrelationId)) {
			if (entry.ReadForStage != -1 && !_orderedStage[entry.ReadForStage])
				throw new InvalidOperationException("Cannot set busy correlation id at non-ordered stage");
			if (entry.BusyCorrelationId != null)
				throw new InvalidOperationException("Busy correlation id has been already set");

			entry.BusyCorrelationId = newCorrelationId;
			if (newCorrelationId != null) {
				if (_correlationLastEntries.TryGetValue(newCorrelationId, out var lastEntry)) {
					if (entry.Sequence < lastEntry.Sequence)
						//NOTE: should never happen as we require ordered stage or initialization
						throw new InvalidOperationException("Cannot inject task correlation id before another task with the same correlation id");
					lastEntry.NextByCorrelation = entry;
					entry.PreviousByCorrelation = lastEntry;
					_correlationLastEntries[newCorrelationId] = entry;
				} else
					_correlationLastEntries.Add(newCorrelationId, entry);
			}
		}
	}

	private static void MarkCompletedTask(TaskEntry entry) {
		entry.Completed = true;
	}

	public void Initialize() {
		_correlationLastEntries.Clear();
		_byUnorderedStageFirst = new StageEntry[_orderedStage.Length];
		_byUnorderedStageLast = new StageEntry[_orderedStage.Length];
		_byOrderedStageLast = new TaskEntry[_orderedStage.Length];
		Count = 0;
		_first = null;
		_last = null;
	}
}

public abstract class StagedTask(object initialCorrelationId) {
	public readonly object InitialCorrelationId = initialCorrelationId;

	public abstract void Process(int onStage, Action<int, object> readyForStage);
}
