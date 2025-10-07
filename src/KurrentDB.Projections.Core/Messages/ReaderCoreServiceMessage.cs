// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Projections.Core.Messages;

public static partial class ReaderCoreServiceMessage {
	[DerivedMessage(ProjectionMessage.ReaderCoreService)]
	public partial class StartReader(Guid instanceCorrelationId) : Message {
		public Guid InstanceCorrelationId { get; } = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.ReaderCoreService)]
	public partial class StopReader(Guid queueId) : Message {
		public Guid QueueId { get; } = queueId;
	}
}
