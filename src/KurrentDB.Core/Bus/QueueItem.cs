// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messaging;
using KurrentDB.Core.Time;

namespace KurrentDB.Core.Bus;

public struct QueueItem {
	public QueueItem(Instant enqueuedAt, Message message) {
		EnqueuedAt = enqueuedAt;
		Message = message;
	}

	public Instant EnqueuedAt { get; }
	public Message Message { get; }
}
