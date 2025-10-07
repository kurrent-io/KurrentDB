// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
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
	protected readonly string _name = name;
	protected readonly ProjectionVersion _projectionVersion = projectionVersion;
	protected readonly ILogger _logger = logger;
	protected readonly int _maxProjectionStateSize = maxProjectionStateSize;

	public CoreProjection Create(
		Guid projectionCorrelationId,
		IPublisher inputQueue,
		Guid workerId,
		ClaimsPrincipal runAs,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ITimeProvider timeProvider) {
		ArgumentNullException.ThrowIfNull(inputQueue);
		ArgumentNullException.ThrowIfNull(publisher);
		ArgumentNullException.ThrowIfNull(ioDispatcher);
		ArgumentNullException.ThrowIfNull(timeProvider);

		var namingBuilder = new ProjectionNamesBuilder(_name, GetSourceDefinition());

		var coreProjectionCheckpointWriter =
			new CoreProjectionCheckpointWriter(
				namingBuilder.MakeCheckpointStreamName(),
				ioDispatcher,
				_projectionVersion,
				namingBuilder.EffectiveProjectionName);

		var partitionStateCache = new PartitionStateCache();

		return new CoreProjection(
			this,
			_projectionVersion,
			projectionCorrelationId,
			inputQueue,
			workerId,
			runAs,
			publisher,
			ioDispatcher,
			_logger,
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
	public abstract bool GetProducesRunningResults();
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
