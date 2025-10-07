// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Services.Processing.Subscriptions;

public class ReaderSubscriptionOptions(
	long checkpointUnhandledBytesThreshold,
	int? checkpointProcessedEventsThreshold,
	int checkpointAfterMs,
	bool stopOnEof,
	int? stopAfterNEvents,
	bool enableContentTypeValidation) {
	public long CheckpointUnhandledBytesThreshold { get; } = checkpointUnhandledBytesThreshold;

	public int? CheckpointProcessedEventsThreshold { get; } = checkpointProcessedEventsThreshold;

	public int CheckpointAfterMs { get; } = checkpointAfterMs;

	public bool StopOnEof { get; } = stopOnEof;

	public int? StopAfterNEvents { get; } = stopAfterNEvents;

	public bool EnableContentTypeValidation { get; } = enableContentTypeValidation;
}
