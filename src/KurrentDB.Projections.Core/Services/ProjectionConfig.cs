// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Projections.Core.Common;

namespace KurrentDB.Projections.Core.Services;

public class ProjectionConfig {
	public ProjectionConfig(ClaimsPrincipal runAs, int checkpointHandledThreshold, int checkpointUnhandledBytesThreshold,
		int pendingEventsThreshold, int maxWriteBatchLength, bool emitEventEnabled, bool checkpointsEnabled, bool stopOnEof, bool trackEmittedStreams,
		int checkpointAfterMs, int maximumAllowedWritesInFlight, int? projectionExecutionTimeout) {
		if (checkpointsEnabled) {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkpointHandledThreshold);
			if (checkpointUnhandledBytesThreshold < checkpointHandledThreshold)
				throw new ArgumentException("Checkpoint threshold cannot be less than checkpoint handled threshold");
		} else {
			ArgumentOutOfRangeException.ThrowIfNotEqual(checkpointHandledThreshold, 0);
			ArgumentOutOfRangeException.ThrowIfNotEqual(checkpointUnhandledBytesThreshold, 0);
		}

		if (maximumAllowedWritesInFlight < AllowedWritesInFlight.Unbounded) {
			throw new ArgumentException($"The Maximum Number of Allowed Writes in Flight cannot be less than {AllowedWritesInFlight.Unbounded}");
		}

		if (projectionExecutionTimeout <= 0) {
			throw new ArgumentException($"The projection execution timeout should be positive. Found : {projectionExecutionTimeout}");
		}

		RunAs = runAs;
		CheckpointHandledThreshold = checkpointHandledThreshold;
		CheckpointUnhandledBytesThreshold = checkpointUnhandledBytesThreshold;
		PendingEventsThreshold = pendingEventsThreshold;
		MaxWriteBatchLength = maxWriteBatchLength;
		EmitEventEnabled = emitEventEnabled;
		CheckpointsEnabled = checkpointsEnabled;
		StopOnEof = stopOnEof;
		TrackEmittedStreams = trackEmittedStreams;
		CheckpointAfterMs = checkpointAfterMs;
		MaximumAllowedWritesInFlight = maximumAllowedWritesInFlight;
		ProjectionExecutionTimeout = projectionExecutionTimeout;
	}

	public int CheckpointHandledThreshold { get; }

	public int CheckpointUnhandledBytesThreshold { get; }

	public int MaxWriteBatchLength { get; }

	public bool EmitEventEnabled { get; }

	public bool CheckpointsEnabled { get; }

	public int PendingEventsThreshold { get; }

	public bool StopOnEof { get; }

	public ClaimsPrincipal RunAs { get; }

	public bool TrackEmittedStreams { get; }

	public int CheckpointAfterMs { get; }

	public int MaximumAllowedWritesInFlight { get; }

	public int? ProjectionExecutionTimeout { get; }
}
