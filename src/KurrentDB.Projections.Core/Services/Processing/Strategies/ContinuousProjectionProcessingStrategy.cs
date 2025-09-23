// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public class ContinuousProjectionProcessingStrategy(
	string name,
	ProjectionVersion projectionVersion,
	IProjectionStateHandler stateHandler,
	ProjectionConfig projectionConfig,
	IQuerySources sourceDefinition,
	ILogger logger,
	ReaderSubscriptionDispatcher subscriptionDispatcher,
	bool enableContentTypeValidation,
	int maxProjectionStateSize)
	: DefaultProjectionProcessingStrategy(
		name, projectionVersion, stateHandler, projectionConfig, sourceDefinition, logger,
		subscriptionDispatcher, enableContentTypeValidation, maxProjectionStateSize) {
	public override bool GetStopOnEof() => false;

	public override bool GetUseCheckpoints() => ProjectionConfig.CheckpointsEnabled;

	protected override bool GetProducesRunningResults() => SourceDefinition.ProducesResults;

	protected override IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher,
		Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder,
		PartitionStateCache partitionStateCache,
		CoreProjection coreProjection,
		IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase)
		=> [firstPhase];

	protected override IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder)
		=> SourceDefinition.ProducesResults
			? new ResultEventEmitter(namingBuilder)
			: new NoopResultEventEmitter();
}
