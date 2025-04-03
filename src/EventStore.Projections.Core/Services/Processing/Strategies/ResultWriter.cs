// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Services.Processing.Strategies;

public class ResultWriter : IResultWriter {
	private readonly IResultEventEmitter _resultEventEmitter;
	private readonly IEmittedEventWriter _coreProjectionCheckpointManager;
	private readonly bool _producesRunningResults;
	private readonly CheckpointTag _zeroCheckpointTag;
	private readonly string _partitionCatalogStreamName;

	public ResultWriter(
		IResultEventEmitter resultEventEmitter, IEmittedEventWriter coreProjectionCheckpointManager,
		bool producesRunningResults, CheckpointTag zeroCheckpointTag, string partitionCatalogStreamName) {
		_resultEventEmitter = resultEventEmitter;
		_coreProjectionCheckpointManager = coreProjectionCheckpointManager;
		_producesRunningResults = producesRunningResults;
		_zeroCheckpointTag = zeroCheckpointTag;
		_partitionCatalogStreamName = partitionCatalogStreamName;
	}

	public void WriteEofResult(
		Guid subscriptionId, string partition, string resultBody, CheckpointTag causedBy, Guid causedByGuid,
		string correlationId) {
		if (resultBody != null)
			WriteResult(partition, resultBody, causedBy, causedByGuid, correlationId);
	}

	private void WriteResult(
		string partition, string resultBody, CheckpointTag causedBy, Guid causedByGuid, string correlationId) {
		var resultEvents = ResultUpdated(partition, resultBody, causedBy);
		if (resultEvents != null)
			_coreProjectionCheckpointManager.EventsEmitted(resultEvents, causedByGuid, correlationId);
	}

	public void WriteRunningResult(EventProcessedResult result) {
		if (!_producesRunningResults)
			return;
		var oldState = result.OldState;
		var newState = result.NewState;
		var resultBody = newState.Result;
		if (oldState.Result != resultBody) {
			var partition = result.Partition;
			var causedBy = newState.CausedBy;
			WriteResult(
				partition, resultBody, causedBy, result.CausedBy, result.CorrelationId);
		}
	}

	private EmittedEventEnvelope[] ResultUpdated(string partition, string result, CheckpointTag causedBy) {
		return _resultEventEmitter.ResultUpdated(partition, result, causedBy);
	}

	protected EmittedEventEnvelope[] RegisterNewPartition(string partition, CheckpointTag at) {
		return new[] {
			new EmittedEventEnvelope(
				new EmittedDataEvent(
					_partitionCatalogStreamName, Guid.NewGuid(), "$partition", false, partition,
					null, at, null))
		};
	}

	public void AccountPartition(EventProcessedResult result) {
		if (_producesRunningResults)
			if (result.Partition != "" && result.OldState.CausedBy == _zeroCheckpointTag) {
				var resultEvents = RegisterNewPartition(result.Partition, result.CheckpointTag);
				if (resultEvents != null)
					_coreProjectionCheckpointManager.EventsEmitted(
						resultEvents, Guid.Empty, correlationId: null);
			}
	}

	public void EventsEmitted(
		EmittedEventEnvelope[] scheduledWrites, Guid causedBy, string correlationId) {
		_coreProjectionCheckpointManager.EventsEmitted(
			scheduledWrites, causedBy, correlationId);
	}

	public void WriteProgress(Guid subscriptionId, float progress) {
		// intentionally does nothing
	}
}
