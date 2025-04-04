// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Bus;

/// <summary>
/// A struct providing information for <see cref="ISingleConsumerMessageQueue.TryDequeue"/> result.
/// </summary>
public struct QueueBatchDequeueResult {
	public int DequeueCount;
	public int EstimateCurrentQueueCount;
}
