// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public abstract class ProjectionProcessingStrategy {
	public abstract ICoreProjectionControl Create(
		Guid projectionCorrelationId,
		IPublisher inputQueue,
		Guid workerId,
		ClaimsPrincipal runAs,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ITimeProvider timeProvider);
}
