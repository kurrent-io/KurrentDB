// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public abstract class EventReaderBasedProjectionProcessingStrategy(
	string name,
	ProjectionVersion projectionVersion,
	ProjectionConfig projectionConfig,
	IQuerySources sourceDefinition,
	ILogger logger,
	ReaderSubscriptionDispatcher subscriptionDispatcher,
	bool enableContentTypeValidation,
	int maxProjectionStateSize)
	: ProjectionProcessingStrategy(name, projectionVersion, logger, maxProjectionStateSize) {
	protected readonly ProjectionConfig ProjectionConfig = projectionConfig;
	protected readonly IQuerySources SourceDefinition = sourceDefinition;
	private readonly bool _isBiState = sourceDefinition.IsBiState;
	protected readonly bool EnableContentTypeValidation = enableContentTypeValidation;

	public sealed override IProjectionProcessingPhase[] CreateProcessingPhases(
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
		var readerStrategy = CreateReaderStrategy(timeProvider);
		var zeroCheckpointTag = readerStrategy.PositionTagger.MakeZeroCheckpointTag();
		var checkpointManager = CreateCheckpointManager(
			projectionCorrelationId,
			publisher,
			ioDispatcher,
			namingBuilder,
			coreProjectionCheckpointWriter,
			readerStrategy);

		var resultWriter = CreateFirstPhaseResultWriter(checkpointManager as IEmittedEventWriter, zeroCheckpointTag, namingBuilder);

		var emittedStreamsTracker = new EmittedStreamsTracker(ioDispatcher, ProjectionConfig, namingBuilder);

		var firstPhase = CreateFirstProcessingPhase(
			publisher,
			inputQueue,
			projectionCorrelationId,
			partitionStateCache,
			updateStatistics,
			coreProjection,
			subscriptionDispatcher,
			zeroCheckpointTag,
			checkpointManager,
			readerStrategy,
			resultWriter,
			emittedStreamsTracker);

		return CreateProjectionProcessingPhases(
			publisher,
			projectionCorrelationId,
			namingBuilder,
			partitionStateCache,
			coreProjection,
			ioDispatcher,
			firstPhase);
	}

	protected abstract IProjectionProcessingPhase CreateFirstProcessingPhase(
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
		IEmittedStreamsTracker emittedStreamsTracker);

	protected IReaderStrategy CreateReaderStrategy(ITimeProvider timeProvider)
		=> ReaderStrategy.Create(Name, 0, SourceDefinition, timeProvider, ProjectionConfig.RunAs);

	protected abstract IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder);

	protected abstract IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher,
		Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder,
		PartitionStateCache partitionStateCache,
		CoreProjection coreProjection,
		IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase);

	protected override IQuerySources GetSourceDefinition() => SourceDefinition;

	public override bool GetRequiresRootPartition() => !(SourceDefinition.ByStreams || SourceDefinition.ByCustomPartitions) || _isBiState;

	public override void EnrichStatistics(ProjectionStatistics info) {
		//TODO: get rid of this cast
		info.ResultStreamName = SourceDefinition.ResultStreamNameOption;
	}

	protected ICoreProjectionCheckpointManager CreateCheckpointManager(
		Guid projectionCorrelationId,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ProjectionNamesBuilder namingBuilder,
		CoreProjectionCheckpointWriter coreProjectionCheckpointWriter,
		IReaderStrategy readerStrategy) {
		var emitAny = ProjectionConfig.EmitEventEnabled;

		//NOTE: not emitting one-time/transient projections are always handled by default checkpoint manager
		// as they don't depend on stable event order
		return emitAny && !readerStrategy.IsReadingOrderRepeatable
			? new MultiStreamMultiOutputCheckpointManager(
				publisher, projectionCorrelationId, ProjectionVersion, ProjectionConfig.RunAs, ioDispatcher,
				ProjectionConfig, Name, readerStrategy.PositionTagger, namingBuilder,
				ProjectionConfig.CheckpointsEnabled,
				coreProjectionCheckpointWriter, MaxProjectionStateSize)
			: new DefaultCheckpointManager(
				publisher, projectionCorrelationId, ProjectionVersion, ProjectionConfig.RunAs, ioDispatcher,
				ProjectionConfig, Name, readerStrategy.PositionTagger, namingBuilder,
				ProjectionConfig.CheckpointsEnabled,
				coreProjectionCheckpointWriter, MaxProjectionStateSize);
	}

	protected IResultWriter CreateFirstPhaseResultWriter(
		IEmittedEventWriter emittedEventWriter,
		CheckpointTag zeroCheckpointTag,
		ProjectionNamesBuilder namingBuilder)
		=> new ResultWriter(
			CreateFirstPhaseResultEmitter(namingBuilder), emittedEventWriter, GetProducesRunningResults(),
			zeroCheckpointTag, namingBuilder.GetPartitionCatalogStreamName());
}
