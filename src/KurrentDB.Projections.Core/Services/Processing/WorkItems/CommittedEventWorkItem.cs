// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

internal class CommittedEventWorkItem : WorkItem {
	private readonly EventReaderSubscriptionMessage.CommittedEventReceived _message;
	private string _partition;
	private readonly IEventProcessingProjectionPhase _projection;
	private readonly StatePartitionSelector _statePartitionSelector;
	private EventProcessedResult _eventProcessedResult;

	public CommittedEventWorkItem(
		IEventProcessingProjectionPhase projection, EventReaderSubscriptionMessage.CommittedEventReceived message,
		StatePartitionSelector statePartitionSelector)
		: base(null) {
		_projection = projection;
		_statePartitionSelector = statePartitionSelector;
		_message = message;
		RequiresRunning = true;
	}

	protected override void RecordEventOrder() {
		_projection.RecordEventOrder(_message.Data, _message.CheckpointTag, () => NextStage());
	}

	protected override void GetStatePartition() {
		_partition = _statePartitionSelector.GetStatePartition(_message);
		if (_partition == null)
			// skip processing of events not mapped to any partition
			NextStage();
		else
			NextStage(_partition);
	}

	protected override void Load() {
		if (_partition == null) {
			NextStage();
			return;
		}

		// we load a partition state even if stopping etc. should we skip?
		_projection.BeginGetPartitionStateAt(_partition, _message.CheckpointTag, LoadCompleted, lockLoaded: true);
	}

	private void LoadCompleted(PartitionState state) {
		NextStage();
	}

	protected override void ProcessEvent() {
		if (_partition == null) {
			NextStage();
			return;
		}

		var eventProcessedResult = _projection.ProcessCommittedEvent(_message, _partition);
		if (eventProcessedResult != null)
			SetEventProcessedResult(eventProcessedResult);
		NextStage();
	}

	protected override void WriteOutput() {
		if (_partition == null) {
			NextStage();
			return;
		}

		_projection.FinalizeEventProcessing(_eventProcessedResult, _message.CheckpointTag, _message.Progress);
		NextStage();
	}

	private void SetEventProcessedResult(EventProcessedResult eventProcessedResult) {
		_eventProcessedResult = eventProcessedResult;
	}
}
