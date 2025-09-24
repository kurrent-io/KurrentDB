// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

public abstract class WorkItem(object initialCorrelationId) : StagedTask(initialCorrelationId) {
	private const int LastStage = 5;
	private Action<int, object> _complete;
	private int _onStage;
	private CheckpointTag _checkpointTag;
	private CoreProjectionQueue _queue;
	private object _lastStageCorrelationId;
	protected bool RequiresRunning;

	public override void Process(int onStage, Action<int, object> readyForStage) {
		if (_checkpointTag == null)
			throw new InvalidOperationException("CheckpointTag has not been initialized");
		_complete = readyForStage;
		_onStage = onStage;
		//TODO: ??
		if (RequiresRunning && !_queue.IsRunning) {
			NextStage();
			return;
		}

		switch (onStage) {
			case 0:
				RecordEventOrder();
				break;
			case 1:
				GetStatePartition();
				break;
			case 2:
				Load();
				break;
			case 3:
				ProcessEvent();
				break;
			case 4:
				WriteOutput();
				break;
			case 5:
				CompleteItem();
				break;
			default:
				throw new NotSupportedException();
		}
	}

	protected virtual void RecordEventOrder() => NextStage();

	protected virtual void GetStatePartition() => NextStage();

	protected virtual void Load() => NextStage();

	protected virtual void ProcessEvent() => NextStage();

	protected virtual void WriteOutput() => NextStage();

	protected virtual void CompleteItem() => NextStage();

	protected void NextStage(object newCorrelationId = null) {
		_lastStageCorrelationId = newCorrelationId ?? _lastStageCorrelationId ?? InitialCorrelationId;
		_complete(_onStage == LastStage ? -1 : _onStage + 1, _lastStageCorrelationId);
	}

	public void SetCheckpointTag(CheckpointTag checkpointTag) {
		_checkpointTag = checkpointTag;
	}

	public void SetProjectionQueue(CoreProjectionQueue coreProjectionQueue) {
		_queue = coreProjectionQueue;
	}
}
