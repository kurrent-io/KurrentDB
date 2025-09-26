// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public abstract class DefaultProjectionProcessingStrategy(
	string name,
	ProjectionVersion projectionVersion,
	IProjectionStateHandler stateHandler,
	ProjectionConfig projectionConfig,
	IQuerySources sourceDefinition,
	ILogger logger,
	ReaderSubscriptionDispatcher subscriptionDispatcher,
	bool enableContentTypeValidation,
	int maxProjectionStateSize)
	: EventReaderBasedProjectionProcessingStrategy(name, projectionVersion, projectionConfig, sourceDefinition, logger,
		subscriptionDispatcher,
		enableContentTypeValidation, maxProjectionStateSize) {
	protected override IProjectionProcessingPhase CreateFirstProcessingPhase(
		IPublisher publisher,
		IPublisher inputQueue,
		Guid projectionCorrelationId,
		PartitionStateCache partitionStateCache,
		Action updateStatistics,
		CoreProjection coreProjection,
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		CheckpointTag zeroCheckpointTag,
		ICoreProjectionCheckpointManager checkpointManager,
		IReaderStrategy readerStrategy,
		IResultWriter resultWriter,
		IEmittedStreamsTracker emittedStreamsTracker) {
		var statePartitionSelector = CreateStatePartitionSelector();

		var orderedPartitionProcessing = _sourceDefinition.ByStreams && _sourceDefinition.IsBiState;
		return new EventProcessingProjectionProcessingPhase(
			coreProjection,
			projectionCorrelationId,
			publisher,
			inputQueue,
			_projectionConfig,
			updateStatistics,
			stateHandler,
			partitionStateCache,
			_sourceDefinition.DefinesStateTransform,
			_name,
			_logger,
			zeroCheckpointTag,
			checkpointManager,
			statePartitionSelector,
			subscriptionDispatcher,
			readerStrategy,
			resultWriter,
			_projectionConfig.CheckpointsEnabled,
			GetStopOnEof(),
			_sourceDefinition.IsBiState,
			orderedPartitionProcessing: orderedPartitionProcessing,
			emittedStreamsTracker: emittedStreamsTracker,
			enableContentTypeValidation: _enableContentTypeValidation);
	}

	protected StatePartitionSelector CreateStatePartitionSelector() {
		return _sourceDefinition.ByCustomPartitions
			? new ByHandleStatePartitionSelector(stateHandler)
			: _sourceDefinition.ByStreams
				? new ByStreamStatePartitionSelector()
				: new NoopStatePartitionSelector();
	}
}
