// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Options;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Metrics;

namespace KurrentDB.Projections.Core;

public class ProjectionsStandardComponents(
	int projectionWorkerThreadCount,
	ProjectionType runProjections,
	ISubscriber leaderOutputBus,
	IPublisher leaderOutputQueue,
	ISubscriber leaderInputBus,
	IPublisher leaderInputQueue,
	bool faultOutOfOrderProjections,
	int projectionCompilationTimeout,
	int projectionExecutionTimeout,
	int maxProjectionStateSize,
	ProjectionTrackers projectionTrackers) {
	public int ProjectionWorkerThreadCount { get; } = projectionWorkerThreadCount;
	public ProjectionType RunProjections { get; } = runProjections;
	public ISubscriber LeaderOutputBus { get; } = leaderOutputBus;
	public IPublisher LeaderOutputQueue { get; } = leaderOutputQueue;
	public IPublisher LeaderInputQueue { get; } = leaderInputQueue;
	public ISubscriber LeaderInputBus { get; } = leaderInputBus;
	public bool FaultOutOfOrderProjections { get; } = faultOutOfOrderProjections;
	public int ProjectionCompilationTimeout { get; } = projectionCompilationTimeout;
	public int ProjectionExecutionTimeout { get; } = projectionExecutionTimeout;
	public int MaxProjectionStateSize { get; } = maxProjectionStateSize;
	public ProjectionTrackers ProjectionTrackers { get; } = projectionTrackers;
}
