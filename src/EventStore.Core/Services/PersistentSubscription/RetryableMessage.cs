// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.PersistentSubscription;

public struct RetryableMessage {
	public readonly Guid MessageId;
	public readonly DateTime DueTime;

	public RetryableMessage(Guid messageId, DateTime dueTime) {
		MessageId = messageId;
		DueTime = dueTime;
	}
}
