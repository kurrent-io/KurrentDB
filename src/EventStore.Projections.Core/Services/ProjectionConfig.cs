// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Common;
using System;
using System.Security.Claims;
using EventStore.Core;

namespace EventStore.Projections.Core.Services;

public class ProjectionConfig {
	private readonly ClaimsPrincipal _runAs;
	private readonly int _checkpointHandledThreshold;
	private readonly int _checkpointUnhandledBytesThreshold;
	private readonly int _pendingEventsThreshold;
	private readonly int _maxWriteBatchLength;
	private readonly bool _emitEventEnabled;
	private readonly bool _checkpointsEnabled;
	private readonly bool _createTempStreams;
	private readonly bool _stopOnEof;
	private readonly bool _trackEmittedStreams;
	private readonly int _checkpointAfterMs;
	private readonly int _maximumAllowedWritesInFlight;

	public ProjectionConfig(ClaimsPrincipal runAs, int checkpointHandledThreshold, int checkpointUnhandledBytesThreshold,
		int pendingEventsThreshold, int maxWriteBatchLength, bool emitEventEnabled, bool checkpointsEnabled,
		bool createTempStreams, bool stopOnEof, bool trackEmittedStreams,
		int checkpointAfterMs, int maximumAllowedWritesInFlight, int? projectionExecutionTimeout) {
		if (checkpointsEnabled) {
			if (checkpointHandledThreshold <= 0)
				throw new ArgumentOutOfRangeException("checkpointHandledThreshold");
			if (checkpointUnhandledBytesThreshold < checkpointHandledThreshold)
				throw new ArgumentException(
					"Checkpoint threshold cannot be less than checkpoint handled threshold");
		} else {
			if (checkpointHandledThreshold != 0)
				throw new ArgumentOutOfRangeException("checkpointHandledThreshold must be 0");
			if (checkpointUnhandledBytesThreshold != 0)
				throw new ArgumentException("checkpointUnhandledBytesThreshold must be 0");
		}

		if (maximumAllowedWritesInFlight < AllowedWritesInFlight.Unbounded) {
			throw new ArgumentException(
				$"The Maximum Number of Allowed Writes in Flight cannot be less than {AllowedWritesInFlight.Unbounded}");
		}

		if (projectionExecutionTimeout is not null && projectionExecutionTimeout <= 0) {
			throw new ArgumentException(
				$"The projection execution timeout should be positive. Found : {projectionExecutionTimeout}");
		}

		_runAs = runAs;
		_checkpointHandledThreshold = checkpointHandledThreshold;
		_checkpointUnhandledBytesThreshold = checkpointUnhandledBytesThreshold;
		_pendingEventsThreshold = pendingEventsThreshold;
		_maxWriteBatchLength = maxWriteBatchLength;
		_emitEventEnabled = emitEventEnabled;
		_checkpointsEnabled = checkpointsEnabled;
		_createTempStreams = createTempStreams;
		_stopOnEof = stopOnEof;
		_trackEmittedStreams = trackEmittedStreams;
		_checkpointAfterMs = checkpointAfterMs;
		_maximumAllowedWritesInFlight = maximumAllowedWritesInFlight;
		ProjectionExecutionTimeout = projectionExecutionTimeout;
	}

	public int CheckpointHandledThreshold {
		get { return _checkpointHandledThreshold; }
	}

	public int CheckpointUnhandledBytesThreshold {
		get { return _checkpointUnhandledBytesThreshold; }
	}

	public int MaxWriteBatchLength {
		get { return _maxWriteBatchLength; }
	}

	public bool EmitEventEnabled {
		get { return _emitEventEnabled; }
	}

	public bool CheckpointsEnabled {
		get { return _checkpointsEnabled; }
	}

	public int PendingEventsThreshold {
		get { return _pendingEventsThreshold; }
	}

	public bool CreateTempStreams {
		get { return _createTempStreams; }
	}

	public bool StopOnEof {
		get { return _stopOnEof; }
	}

	public ClaimsPrincipal RunAs {
		get { return _runAs; }
	}

	public bool TrackEmittedStreams {
		get { return _trackEmittedStreams; }
	}

	public int CheckpointAfterMs {
		get { return _checkpointAfterMs; }
	}

	public int MaximumAllowedWritesInFlight {
		get { return _maximumAllowedWritesInFlight; }
	}

	public int? ProjectionExecutionTimeout { get; }
}
