// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Services.Processing.Strategies;

public interface IResultWriter {
	//NOTE: subscriptionId should not be here.  Reconsider how to pass it to follower projection result writer
	void WriteEofResult(
		Guid subscriptionId, string partition, string resultBody, CheckpointTag causedBy, Guid causedByGuid,
		string correlationId);

	void WriteRunningResult(EventProcessedResult result);

	void AccountPartition(EventProcessedResult result);

	void EventsEmitted(EmittedEventEnvelope[] scheduledWrites, Guid causedBy, string correlationId);

	void WriteProgress(Guid subscriptionId, float progress);
}
