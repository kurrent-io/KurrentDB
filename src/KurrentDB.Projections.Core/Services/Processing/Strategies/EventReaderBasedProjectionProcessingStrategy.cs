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

public abstract class EventReaderBasedProjectionProcessingStrategy : ProjectionProcessingStrategy {
	protected readonly ProjectionConfig _projectionConfig;
	protected readonly IQuerySources _sourceDefinition;
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private readonly bool _isBiState;
	protected readonly bool _enableContentTypeValidation;

	protected EventReaderBasedProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition,
		ILogger logger,
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		bool enableContentTypeValidation,
		int maxProjectionStateSize)
		: base(name, projectionVersion, logger, maxProjectionStateSize) {
		_projectionConfig = projectionConfig;
		_sourceDefinition = sourceDefinition;
		_subscriptionDispatcher = subscriptionDispatcher;
		_isBiState = sourceDefinition.IsBiState;
		_enableContentTypeValidation = enableContentTypeValidation;
	}

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
		var resultWriter = CreateFirstPhaseResultWriter(
			checkpointManager as IEmittedEventWriter,
			zeroCheckpointTag,
			namingBuilder);
		var emittedStreamsTracker = new EmittedStreamsTracker(ioDispatcher, _projectionConfig, namingBuilder);
		var firstPhase = CreateFirstProcessingPhase(
			publisher,
			inputQueue,
			projectionCorrelationId,
			partitionStateCache,
			updateStatistics,
			coreProjection,
			_subscriptionDispatcher,
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

	private IReaderStrategy CreateReaderStrategy(ITimeProvider timeProvider)
		=> ReaderStrategy.Create(_name, 0, _sourceDefinition, timeProvider, _projectionConfig.RunAs);

	protected abstract IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder);

	protected abstract IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher,
		Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder,
		PartitionStateCache partitionStateCache,
		CoreProjection coreProjection,
		IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase);

	protected override IQuerySources GetSourceDefinition() => _sourceDefinition;

	public override bool GetRequiresRootPartition() => !(_sourceDefinition.ByStreams || _sourceDefinition.ByCustomPartitions) || _isBiState;

	public override void EnrichStatistics(ProjectionStatistics info) {
		//TODO: get rid of this cast
		info.ResultStreamName = _sourceDefinition.ResultStreamNameOption;
	}

	private ICoreProjectionCheckpointManager CreateCheckpointManager(
		Guid projectionCorrelationId,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ProjectionNamesBuilder namingBuilder,
		CoreProjectionCheckpointWriter coreProjectionCheckpointWriter,
		IReaderStrategy readerStrategy) {
		var emitAny = _projectionConfig.EmitEventEnabled;

		//NOTE: not emitting one-time/transient projections are always handled by default checkpoint manager
		// as they don't depend on stable event order
		return emitAny && !readerStrategy.IsReadingOrderRepeatable
			? new MultiStreamMultiOutputCheckpointManager(
				publisher, projectionCorrelationId, _projectionVersion, _projectionConfig.RunAs, ioDispatcher,
				_projectionConfig, readerStrategy.PositionTagger, namingBuilder,
				_projectionConfig.CheckpointsEnabled,
				coreProjectionCheckpointWriter, _maxProjectionStateSize)
			: new DefaultCheckpointManager(
				publisher, projectionCorrelationId, _projectionVersion, _projectionConfig.RunAs, ioDispatcher,
				_projectionConfig, readerStrategy.PositionTagger, namingBuilder,
				_projectionConfig.CheckpointsEnabled,
				coreProjectionCheckpointWriter, _maxProjectionStateSize);
	}

	protected IResultWriter CreateFirstPhaseResultWriter(IEmittedEventWriter emittedEventWriter,
		CheckpointTag zeroCheckpointTag,
		ProjectionNamesBuilder namingBuilder) {
		return new ResultWriter(
			CreateFirstPhaseResultEmitter(namingBuilder), emittedEventWriter, GetProducesRunningResults(),
			zeroCheckpointTag, namingBuilder.GetPartitionCatalogStreamName());
	}
}
