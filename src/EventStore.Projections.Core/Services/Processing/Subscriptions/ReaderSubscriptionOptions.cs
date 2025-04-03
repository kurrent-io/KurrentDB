// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Services.Processing.Subscriptions;

public class ReaderSubscriptionOptions {
	private readonly long _checkpointUnhandledBytesThreshold;
	private readonly int? _checkpointProcessedEventsThreshold;
	private readonly int _checkpointAfterMs;
	private readonly bool _stopOnEof;
	private readonly int? _stopAfterNEvents;
	private readonly bool _enableContentTypeValidation;

	public ReaderSubscriptionOptions(
		long checkpointUnhandledBytesThreshold, int? checkpointProcessedEventsThreshold, int checkpointAfterMs,
		bool stopOnEof, int? stopAfterNEvents, bool enableContentTypeValidation) {
		_checkpointUnhandledBytesThreshold = checkpointUnhandledBytesThreshold;
		_checkpointProcessedEventsThreshold = checkpointProcessedEventsThreshold;
		_checkpointAfterMs = checkpointAfterMs;
		_stopOnEof = stopOnEof;
		_stopAfterNEvents = stopAfterNEvents;
		_enableContentTypeValidation = enableContentTypeValidation;
	}

	public long CheckpointUnhandledBytesThreshold {
		get { return _checkpointUnhandledBytesThreshold; }
	}

	public int? CheckpointProcessedEventsThreshold {
		get { return _checkpointProcessedEventsThreshold; }
	}

	public int CheckpointAfterMs {
		get { return _checkpointAfterMs; }
	}

	public bool StopOnEof {
		get { return _stopOnEof; }
	}

	public int? StopAfterNEvents {
		get { return _stopAfterNEvents; }
	}

	public bool EnableContentTypeValidation {
		get { return _enableContentTypeValidation; }
	}
}
