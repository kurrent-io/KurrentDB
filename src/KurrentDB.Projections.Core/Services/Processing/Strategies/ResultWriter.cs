// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public class ResultWriter(
	IResultEventEmitter resultEventEmitter,
	IEmittedEventWriter coreProjectionCheckpointManager,
	bool producesRunningResults,
	CheckpointTag zeroCheckpointTag,
	string partitionCatalogStreamName)
	: IResultWriter {
	private void WriteResult(
		string partition,
		string resultBody,
		CheckpointTag causedBy,
		Guid causedByGuid,
		string correlationId) {
		var resultEvents = ResultUpdated(partition, resultBody, causedBy);
		if (resultEvents != null)
			coreProjectionCheckpointManager.EventsEmitted(resultEvents, causedByGuid, correlationId);
	}

	public void WriteRunningResult(EventProcessedResult result) {
		if (!producesRunningResults)
			return;
		var oldState = result.OldState;
		var newState = result.NewState;
		var resultBody = newState.Result;
		if (oldState.Result != resultBody) {
			var partition = result.Partition;
			var causedBy = newState.CausedBy;
			WriteResult(partition, resultBody, causedBy, result.CausedBy, result.CorrelationId);
		}
	}

	private EmittedEventEnvelope[] ResultUpdated(string partition, string result, CheckpointTag causedBy)
		=> resultEventEmitter.ResultUpdated(partition, result, causedBy);

	private EmittedEventEnvelope[] RegisterNewPartition(string partition, CheckpointTag at) => [
		new(new EmittedDataEvent(partitionCatalogStreamName, Guid.NewGuid(), "$partition", false, partition, null, at, null))
	];

	public void AccountPartition(EventProcessedResult result) {
		if (!producesRunningResults) return;

		if (result.Partition != "" && result.OldState.CausedBy == zeroCheckpointTag) {
			var resultEvents = RegisterNewPartition(result.Partition, result.CheckpointTag);
			if (resultEvents != null)
				coreProjectionCheckpointManager.EventsEmitted(resultEvents, Guid.Empty, correlationId: null);
		}
	}

	public void EventsEmitted(EmittedEventEnvelope[] scheduledWrites, Guid causedBy, string correlationId) {
		coreProjectionCheckpointManager.EventsEmitted(scheduledWrites, causedBy, correlationId);
	}
}
