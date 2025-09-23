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
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public abstract class ProjectionProcessingStrategy(
	string name,
	ProjectionVersion projectionVersion,
	ILogger logger,
	int maxProjectionStateSize) {
	protected readonly string Name = name;
	protected readonly ProjectionVersion ProjectionVersion = projectionVersion;
	protected readonly ILogger Logger = logger;
	protected readonly int MaxProjectionStateSize = maxProjectionStateSize;

	public CoreProjection Create(
		Guid projectionCorrelationId,
		IPublisher inputQueue,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ITimeProvider timeProvider) {
		ArgumentNullException.ThrowIfNull(inputQueue);
		ArgumentNullException.ThrowIfNull(publisher);
		ArgumentNullException.ThrowIfNull(ioDispatcher);
		ArgumentNullException.ThrowIfNull(timeProvider);

		var namingBuilder = new ProjectionNamesBuilder(Name, GetSourceDefinition());

		var coreProjectionCheckpointWriter =
			new CoreProjectionCheckpointWriter(
				namingBuilder.MakeCheckpointStreamName(),
				ioDispatcher,
				ProjectionVersion,
				namingBuilder.EffectiveProjectionName);

		var partitionStateCache = new PartitionStateCache();

		return new CoreProjection(
			this,
			ProjectionVersion,
			projectionCorrelationId,
			inputQueue,
			publisher,
			ioDispatcher,
			Logger,
			namingBuilder,
			coreProjectionCheckpointWriter,
			partitionStateCache,
			namingBuilder.EffectiveProjectionName,
			timeProvider);
	}

	protected abstract IQuerySources GetSourceDefinition();

	public abstract bool GetStopOnEof();
	public abstract bool GetUseCheckpoints();
	public abstract bool GetRequiresRootPartition();
	protected abstract bool GetProducesRunningResults();
	public abstract void EnrichStatistics(ProjectionStatistics info);

	public abstract IProjectionProcessingPhase[] CreateProcessingPhases(
		IPublisher publisher,
		IPublisher inputQueue,
		Guid projectionCorrelationId,
		PartitionStateCache partitionStateCache,
		Action updateStatistics,
		CoreProjection coreProjection,
		ProjectionNamesBuilder namingBuilder,
		ITimeProvider timeProvider,
		IODispatcher ioDispatcher,
		CoreProjectionCheckpointWriter coreProjectionCheckpointWriter);
}
