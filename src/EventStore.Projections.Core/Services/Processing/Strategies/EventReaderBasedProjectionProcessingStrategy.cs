// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Phases;

namespace EventStore.Projections.Core.Services.Processing.Strategies;

public abstract class EventReaderBasedProjectionProcessingStrategy : ProjectionProcessingStrategy {
	protected readonly ProjectionConfig _projectionConfig;
	protected readonly IQuerySources _sourceDefinition;
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private readonly bool _isBiState;
	protected readonly bool _enableContentTypeValidation;

	protected EventReaderBasedProjectionProcessingStrategy(
		string name, ProjectionVersion projectionVersion, ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition, Serilog.ILogger logger, ReaderSubscriptionDispatcher subscriptionDispatcher,
		bool enableContentTypeValidation, int maxProjectionStateSize)
		: base(name, projectionVersion, logger, maxProjectionStateSize) {
		_projectionConfig = projectionConfig;
		_sourceDefinition = sourceDefinition;
		_subscriptionDispatcher = subscriptionDispatcher;
		_isBiState = sourceDefinition.IsBiState;
		_enableContentTypeValidation = enableContentTypeValidation;
	}

	public override sealed IProjectionProcessingPhase[] CreateProcessingPhases(
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
		var definesFold = _sourceDefinition.DefinesFold;

		var readerStrategy = CreateReaderStrategy(timeProvider);

		var zeroCheckpointTag = readerStrategy.PositionTagger.MakeZeroCheckpointTag();

		var checkpointManager = CreateCheckpointManager(
			projectionCorrelationId,
			publisher,
			ioDispatcher,
			namingBuilder,
			coreProjectionCheckpointWriter,
			definesFold,
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
			inputQueue,
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

	protected virtual IReaderStrategy CreateReaderStrategy(ITimeProvider timeProvider) {
		return ReaderStrategy.Create(
			_name,
			0,
			_sourceDefinition,
			timeProvider,
			_projectionConfig.StopOnEof,
			_projectionConfig.RunAs);
	}

	protected abstract IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder);

	protected abstract IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher,
		IPublisher inputQueue,
		Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder,
		PartitionStateCache partitionStateCache,
		CoreProjection coreProjection,
		IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase);

	protected override IQuerySources GetSourceDefinition() {
		return _sourceDefinition;
	}

	public override bool GetRequiresRootPartition() {
		return !(_sourceDefinition.ByStreams || _sourceDefinition.ByCustomPartitions) || _isBiState;
	}

	public override void EnrichStatistics(ProjectionStatistics info) {
		//TODO: get rid of this cast
		info.ResultStreamName = _sourceDefinition.ResultStreamNameOption;
	}

	protected virtual ICoreProjectionCheckpointManager CreateCheckpointManager(
		Guid projectionCorrelationId, IPublisher publisher, IODispatcher ioDispatcher,
		ProjectionNamesBuilder namingBuilder, CoreProjectionCheckpointWriter coreProjectionCheckpointWriter,
		bool definesFold, IReaderStrategy readerStrategy) {
		var emitAny = _projectionConfig.EmitEventEnabled;

		//NOTE: not emitting one-time/transient projections are always handled by default checkpoint manager
		// as they don't depend on stable event order
		if (emitAny && !readerStrategy.IsReadingOrderRepeatable) {
			return new MultiStreamMultiOutputCheckpointManager(
				publisher, projectionCorrelationId, _projectionVersion, _projectionConfig.RunAs, ioDispatcher,
				_projectionConfig, _name, readerStrategy.PositionTagger, namingBuilder,
				_projectionConfig.CheckpointsEnabled, GetProducesRunningResults(), definesFold,
				coreProjectionCheckpointWriter, _maxProjectionStateSize);
		} else {
			return new DefaultCheckpointManager(
				publisher, projectionCorrelationId, _projectionVersion, _projectionConfig.RunAs, ioDispatcher,
				_projectionConfig, _name, readerStrategy.PositionTagger, namingBuilder,
				_projectionConfig.CheckpointsEnabled, GetProducesRunningResults(), definesFold,
				coreProjectionCheckpointWriter, _maxProjectionStateSize);
		}
	}

	protected virtual IResultWriter CreateFirstPhaseResultWriter(
		IEmittedEventWriter emittedEventWriter, CheckpointTag zeroCheckpointTag,
		ProjectionNamesBuilder namingBuilder) {
		return new ResultWriter(
			CreateFirstPhaseResultEmitter(namingBuilder), emittedEventWriter, GetProducesRunningResults(),
			zeroCheckpointTag, namingBuilder.GetPartitionCatalogStreamName());
	}
}
