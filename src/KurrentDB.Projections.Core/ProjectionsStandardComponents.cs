// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Options;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Metrics;

namespace KurrentDB.Projections.Core;

public class ProjectionsStandardComponents {
	public ProjectionsStandardComponents(
		int projectionWorkerThreadCount,
		ProjectionType runProjections,
		ISubscriber leaderOutputBus,
		IPublisher leaderOutputQueue,
		ISubscriber leaderInputBus,
		IPublisher leaderInputQueue,
		bool faultOutOfOrderProjections, int projectionCompilationTimeout, int projectionExecutionTimeout,
		int maxProjectionStateSize,
		ProjectionExecutionTrackers projectionExecutionTrackers,
		ProjectionTrackers projectionTrackers) {
		ProjectionWorkerThreadCount = projectionWorkerThreadCount;
		RunProjections = runProjections;
		LeaderOutputBus = leaderOutputBus;
		LeaderOutputQueue = leaderOutputQueue;
		LeaderInputQueue = leaderInputQueue;
		LeaderInputBus = leaderInputBus;
		FaultOutOfOrderProjections = faultOutOfOrderProjections;
		ProjectionCompilationTimeout = projectionCompilationTimeout;
		ProjectionExecutionTimeout = projectionExecutionTimeout;
		MaxProjectionStateSize = maxProjectionStateSize;
		ProjectionExecutionTrackers = projectionExecutionTrackers;
		ProjectionTrackers = projectionTrackers;
	}

	public int ProjectionWorkerThreadCount { get; }

	public ProjectionType RunProjections { get; }

	public ISubscriber LeaderOutputBus { get; }
	public IPublisher LeaderOutputQueue { get; }

	public IPublisher LeaderInputQueue { get; }
	public ISubscriber LeaderInputBus { get; }

	public bool FaultOutOfOrderProjections { get; }

	public int ProjectionCompilationTimeout { get; }

	public int ProjectionExecutionTimeout { get; }

	public int MaxProjectionStateSize { get; }

	public ProjectionExecutionTrackers ProjectionExecutionTrackers { get; }

	public ProjectionTrackers ProjectionTrackers { get; }
}
