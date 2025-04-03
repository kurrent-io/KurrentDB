// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Phases;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Processing.Strategies;

public class ContinuousProjectionProcessingStrategy : DefaultProjectionProcessingStrategy {
	public ContinuousProjectionProcessingStrategy(
		string name, ProjectionVersion projectionVersion, IProjectionStateHandler stateHandler,
		ProjectionConfig projectionConfig, IQuerySources sourceDefinition, ILogger logger,
		ReaderSubscriptionDispatcher subscriptionDispatcher, bool enableContentTypeValidation, int maxProjectionStateSize)
		: base(
			name, projectionVersion, stateHandler, projectionConfig, sourceDefinition, logger,
			subscriptionDispatcher, enableContentTypeValidation, maxProjectionStateSize) {
	}

	public override bool GetStopOnEof() {
		return false;
	}

	public override bool GetUseCheckpoints() {
		return _projectionConfig.CheckpointsEnabled;
	}

	public override bool GetProducesRunningResults() {
		return _sourceDefinition.ProducesResults;
	}

	protected override IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher,
		IPublisher inputQueue,
		Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder,
		PartitionStateCache partitionStateCache,
		CoreProjection coreProjection,
		IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase) {
		return new IProjectionProcessingPhase[] {firstPhase};
	}

	protected override IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder) {
		return _sourceDefinition.ProducesResults
			? new ResultEventEmitter(namingBuilder)
			: (IResultEventEmitter)new NoopResultEventEmitter();
	}
}
