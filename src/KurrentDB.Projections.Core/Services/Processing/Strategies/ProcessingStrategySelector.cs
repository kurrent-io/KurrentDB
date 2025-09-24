// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public class ProcessingStrategySelector(ReaderSubscriptionDispatcher subscriptionDispatcher, int maxProjectionStateSize) {
	private readonly ILogger _logger = Log.ForContext<ProcessingStrategySelector>();

	public ProjectionProcessingStrategy CreateProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		IQuerySources sourceDefinition,
		ProjectionConfig projectionConfig,
		IProjectionStateHandler stateHandler,
		bool enableContentTypeValidation)
		=> projectionConfig.StopOnEof
			? new QueryProcessingStrategy(
				name,
				projectionVersion,
				stateHandler,
				projectionConfig,
				sourceDefinition,
				_logger,
				subscriptionDispatcher,
				enableContentTypeValidation,
				maxProjectionStateSize)
			: new ContinuousProjectionProcessingStrategy(
				name,
				projectionVersion,
				stateHandler,
				projectionConfig,
				sourceDefinition,
				_logger,
				subscriptionDispatcher,
				enableContentTypeValidation,
				maxProjectionStateSize);
}
