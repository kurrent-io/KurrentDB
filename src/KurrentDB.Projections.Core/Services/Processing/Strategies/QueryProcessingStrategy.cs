// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public class QueryProcessingStrategy : DefaultProjectionProcessingStrategy {
	public QueryProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		IProjectionStateHandler stateHandler,
		ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition,
		ILogger logger,
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		bool enableContentTypeValidation,
		int maxProjectionStateSize)
		: base(
			name, projectionVersion, stateHandler, projectionConfig, sourceDefinition, logger,
			subscriptionDispatcher, enableContentTypeValidation, maxProjectionStateSize) {
	}

	public override bool GetStopOnEof() => true;

	public override bool GetUseCheckpoints() => false;

	public override bool GetProducesRunningResults() => !_sourceDefinition.DefinesFold;

	protected override IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher,
		Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder,
		PartitionStateCache partitionStateCache,
		CoreProjection coreProjection,
		IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase) {
		var coreProjectionCheckpointWriter =
			new CoreProjectionCheckpointWriter(namingBuilder.MakeCheckpointStreamName(), ioDispatcher, _projectionVersion, _name);
		var checkpointManager2 = new DefaultCheckpointManager(
			publisher, projectionCorrelationId, _projectionVersion, SystemAccounts.System, ioDispatcher,
			_projectionConfig, new PhasePositionTagger(1), namingBuilder, GetUseCheckpoints(), coreProjectionCheckpointWriter,
			_maxProjectionStateSize);

		IProjectionProcessingPhase writeResultsPhase;
		if (GetProducesRunningResults())
			writeResultsPhase = new WriteQueryEofProjectionProcessingPhase(
				publisher,
				1,
				namingBuilder.GetResultStreamName(),
				coreProjection,
				partitionStateCache,
				checkpointManager2,
				checkpointManager2,
				firstPhase.EmittedStreamsTracker);
		else
			writeResultsPhase = new WriteQueryResultProjectionProcessingPhase(
				publisher,
				1,
				namingBuilder.GetResultStreamName(),
				coreProjection,
				partitionStateCache,
				checkpointManager2,
				checkpointManager2,
				firstPhase.EmittedStreamsTracker);

		return [firstPhase, writeResultsPhase];
	}

	protected override IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder)
		=> new ResultEventEmitter(namingBuilder);
}
