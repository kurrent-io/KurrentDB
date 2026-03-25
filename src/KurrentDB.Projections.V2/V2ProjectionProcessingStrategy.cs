// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class V2ProjectionProcessingStrategy : ProjectionProcessingStrategy {
	private readonly string _name;
	private readonly ProjectionConfig _projectionConfig;
	private readonly IQuerySources _sourceDefinition;
	private readonly Func<IProjectionStateHandler> _stateHandlerFactory;
	private readonly IPublisher _mainQueue;

	public V2ProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		IProjectionStateHandler stateHandler,
		ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition,
		ILogger logger,
		int maxProjectionStateSize,
		Func<IProjectionStateHandler> stateHandlerFactory,
		IPublisher mainQueue) {

		_name = name;
		_projectionConfig = projectionConfig;
		_sourceDefinition = sourceDefinition;
		_stateHandlerFactory = stateHandlerFactory;
		_mainQueue = mainQueue;
	}

	public override ICoreProjectionControl Create(
		Guid projectionCorrelationId,
		IPublisher inputQueue,
		Guid workerId,
		ClaimsPrincipal runAs,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ITimeProvider timeProvider) {
		return new V2CoreProjection(
			projectionCorrelationId,
			_name,
			publisher,
			inputQueue,
			ioDispatcher,
			runAs,
			_sourceDefinition,
			_stateHandlerFactory,
			_projectionConfig,
			_mainQueue);
	}
}
