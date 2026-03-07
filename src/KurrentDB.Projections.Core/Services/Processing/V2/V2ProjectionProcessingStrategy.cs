// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class V2ProjectionProcessingStrategy : ProjectionProcessingStrategy {
	private readonly IProjectionStateHandler _stateHandler;
	private readonly ProjectionConfig _projectionConfig;
	private readonly IQuerySources _sourceDefinition;

	public V2ProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		IProjectionStateHandler stateHandler,
		ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition,
		ILogger logger,
		int maxProjectionStateSize)
		: base(name, projectionVersion, logger, maxProjectionStateSize) {
		_stateHandler = stateHandler;
		_projectionConfig = projectionConfig;
		_sourceDefinition = sourceDefinition;
	}

	protected override IQuerySources GetSourceDefinition() => _sourceDefinition;

	public override bool GetStopOnEof() => false;

	public override bool GetUseCheckpoints() => true;

	public override bool GetRequiresRootPartition() => false;

	public override bool GetProducesRunningResults() => _sourceDefinition.ProducesResults;

	public override void EnrichStatistics(ProjectionStatistics info) {
		info.ResultStreamName = _sourceDefinition.ResultStreamNameOption;
	}

	public override IProjectionProcessingPhase[] CreateProcessingPhases(
		IPublisher publisher,
		IPublisher inputQueue,
		Guid projectionCorrelationId,
		PartitionStateCache partitionStateCache,
		Action updateStatistics,
		CoreProjection coreProjection,
		ProjectionNamesBuilder namingBuilder,
		ITimeProvider timeProvider,
		IODispatcher ioDispatcher,
		CoreProjectionCheckpointWriter coreProjectionCheckpointWriter) {
		throw new NotSupportedException(
			"V2 engine does not use v1 processing phases. Use ProjectionEngineV2 directly.");
	}
}
