// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.V2;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public class ProcessingStrategySelector {
	private readonly ILogger _logger = Log.ForContext<ProcessingStrategySelector>();
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private readonly int _maxProjectionStateSize;

	public ProcessingStrategySelector(
		ReaderSubscriptionDispatcher subscriptionDispatcher, int maxProjectionStateSize) {
		_subscriptionDispatcher = subscriptionDispatcher;
		_maxProjectionStateSize = maxProjectionStateSize;
	}

	public ProjectionProcessingStrategy CreateProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		ProjectionNamesBuilder namesBuilder,
		IQuerySources sourceDefinition,
		ProjectionConfig projectionConfig,
		string handlerType, string query, bool enableContentTypeValidation,
		int engineVersion,
		Func<IProjectionStateHandler> stateHandlerFactory,
		IPublisher mainQueue) {

		if (engineVersion == ProjectionConstants.EngineV2) {
			return new ProjectionProcessingStrategyV2(
				name, projectionVersion, projectionConfig, sourceDefinition, _logger, _maxProjectionStateSize,
				stateHandlerFactory, mainQueue);
		}

		return projectionConfig.StopOnEof
			? (ProjectionProcessingStrategy)
			new QueryProcessingStrategy(
				name,
				projectionVersion,
				stateHandlerFactory.Invoke(),
				projectionConfig,
				sourceDefinition,
				_logger,
				_subscriptionDispatcher,
				enableContentTypeValidation,
				_maxProjectionStateSize)
			: new ContinuousProjectionProcessingStrategy(
				name,
				projectionVersion,
				stateHandlerFactory.Invoke(),
				projectionConfig,
				sourceDefinition,
				_logger,
				_subscriptionDispatcher,
				enableContentTypeValidation,
				_maxProjectionStateSize);
	}
}
