// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;

namespace EventStore.Projections.Core.Services.Processing;

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
	private class TaskEntry {
		public readonly StagedTask Task;
		public readonly long Sequence;
		public bool Busy;
		public object BusyCorrelationId;
		public TaskEntry PreviousByCorrelation;
		public TaskEntry NextByCorrelation;
		public TaskEntry Next;
		public bool Completed;
		public int ReadForStage;

		public TaskEntry(StagedTask task, long sequence) {
			Task = task;
			ReadForStage = -1;
			Sequence = sequence;
		}

		public override string ToString() {
			return string.Format("ReadForStage: {3},  Busy: {1}, Completed: {2} => {0}", Task, Busy, Completed,
				ReadForStage);
		}
	}

	private class StageEntry {
		public TaskEntry Entry;
		public StageEntry Next;
	}

	private readonly bool[] _orderedStage;
	private readonly Dictionary<object, TaskEntry> _correlationLastEntries = new Dictionary<object, TaskEntry>();
	private StageEntry[] _byUnorderedStageFirst; // null means all processed? so append need to set it?
	private StageEntry[] _byUnorderedStageLast; // null means all processed? so append need to set it?
	private TaskEntry[] _byOrderedStageLast; // null means all processed? so append need to set it?
	private long _sequence = 0;
	private TaskEntry _first;
	private TaskEntry _last;
	private int _count;
	private readonly int _maxStage;
	public event Action EnsureTickPending;

	public StagedProcessingQueue(bool[] orderedStage) {
		_orderedStage = orderedStage.ToArray();
		_byUnorderedStageFirst = new StageEntry[_orderedStage.Length];
		_byUnorderedStageLast = new StageEntry[_orderedStage.Length];
		_byOrderedStageLast = new TaskEntry[_orderedStage.Length];
		_maxStage = _orderedStage.Length - 1;
	}

	public bool IsEmpty {
		get { return _count == 0; }
	}

	public int Count {
		get { return _count; }
	}

	public void Enqueue(StagedTask stagedTask) {
		var entry = new TaskEntry(stagedTask, ++_sequence);
		if (_first == null) {
			_first = entry;
			_last = entry;
			_count = 1;
		} else {
			_last.Next = entry;
			_last = entry;
			_count++;
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
		while (_count > 0 && processed < max) {
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

			if (task.ReadForStage != stageIndex)
				throw new Exception();
			return task;
		}

		return null;
	}

	private void RemoveCompleted() {
		while (_first != null && _first.Completed) {
			var task = _first;
			_first = task.Next;
			if (_first == null)
				_last = null;
			_count--;
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

		if (EnsureTickPending != null)
			EnsureTickPending();
	}

	private void EnqueueForStage(TaskEntry entry, int readyForStage) {
		entry.ReadForStage = readyForStage;
		if (!_orderedStage[readyForStage] && (entry.PreviousByCorrelation == null)) {
			var stageEntry = new StageEntry {Entry = entry, Next = null};
			if (_byUnorderedStageFirst[readyForStage] != null) {
				_byUnorderedStageLast[readyForStage].Next = stageEntry;
				_byUnorderedStageLast[readyForStage] = stageEntry;
			} else {
				_byUnorderedStageFirst[readyForStage] = stageEntry;
				_byUnorderedStageLast[readyForStage] = stageEntry;
			}
		}
	}

	private void AdvanceStage(int stage, TaskEntry entry) {
		if (!_orderedStage[stage]) {
			if (_byUnorderedStageFirst[stage].Entry != entry)
				throw new ArgumentException(
					string.Format("entry is not a head of the queue at the stage {0}", stage), "entry");
			_byUnorderedStageFirst[stage] = _byUnorderedStageFirst[stage].Next;
			if (_byUnorderedStageFirst[stage] == null)
				_byUnorderedStageLast[stage] = null;
		} else {
			if (_byOrderedStageLast[stage] != entry)
				throw new ArgumentException(
					string.Format("entry is not a head of the queue at the stage {0}", stage), "entry");
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
				TaskEntry lastEntry;
				if (_correlationLastEntries.TryGetValue(newCorrelationId, out lastEntry)) {
					if (entry.Sequence < lastEntry.Sequence)
						//NOTE: should never happen as we require ordered stage or initialization
						throw new InvalidOperationException(
							"Cannot inject task correlation id before another task with the same correlation id");
					lastEntry.NextByCorrelation = entry;
					entry.PreviousByCorrelation = lastEntry;
					_correlationLastEntries[newCorrelationId] = entry;
				} else
					_correlationLastEntries.Add(newCorrelationId, entry);
			}
		}
	}

	private void MarkCompletedTask(TaskEntry entry) {
		entry.Completed = true;
	}

	public void Initialize() {
		_correlationLastEntries.Clear();
		_byUnorderedStageFirst = new StageEntry[_orderedStage.Length];
		_byUnorderedStageLast = new StageEntry[_orderedStage.Length];
		_byOrderedStageLast = new TaskEntry[_orderedStage.Length];
		_count = 0;
		_first = null;
		_last = null;
	}
}

public abstract class StagedTask {
	public readonly object InitialCorrelationId;

	protected StagedTask(object initialCorrelationId) {
		InitialCorrelationId = initialCorrelationId;
	}

	public abstract void Process(int onStage, Action<int, object> readyForStage);
}
