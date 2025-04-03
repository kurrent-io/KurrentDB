// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

public abstract class WorkItem : StagedTask {
	private readonly int _lastStage;
	private Action<int, object> _complete;
	private int _onStage;
	private CheckpointTag _checkpointTag;
	private object _lastStageCorrelationId;
	private CoreProjectionQueue _queue;
	protected bool _requiresRunning;

	protected WorkItem(object initialCorrelationId)
		: base(initialCorrelationId) {
		_lastStage = 5;
	}

	protected CoreProjectionQueue Queue {
		get { return _queue; }
	}

	public override void Process(int onStage, Action<int, object> readyForStage) {
		if (_checkpointTag == null)
			throw new InvalidOperationException("CheckpointTag has not been initialized");
		_complete = readyForStage;
		_onStage = onStage;
		//TODO:
		if (_requiresRunning && !Queue.IsRunning)
			NextStage();
		else {
			switch (onStage) {
				case 0:
					RecordEventOrder();
					break;
				case 1:
					GetStatePartition();
					break;
				case 2:
					Load(_checkpointTag);
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
	}

	protected virtual void RecordEventOrder() {
		NextStage();
	}

	protected virtual void GetStatePartition() {
		NextStage();
	}

	protected virtual void Load(CheckpointTag checkpointTag) {
		NextStage();
	}

	protected virtual void ProcessEvent() {
		NextStage();
	}

	protected virtual void WriteOutput() {
		NextStage();
	}


	protected virtual void CompleteItem() {
		NextStage();
	}

	protected void NextStage(object newCorrelationId = null) {
		_lastStageCorrelationId = newCorrelationId ?? _lastStageCorrelationId ?? InitialCorrelationId;
		_complete(_onStage == _lastStage ? -1 : _onStage + 1, _lastStageCorrelationId);
	}

	public void SetCheckpointTag(CheckpointTag checkpointTag) {
		_checkpointTag = checkpointTag;
	}

	public void SetProjectionQueue(CoreProjectionQueue coreProjectionQueue) {
		_queue = coreProjectionQueue;
	}
}
