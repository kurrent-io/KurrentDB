// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messages;

public static partial class ReaderCoreServiceMessage {
	[DerivedMessage(ProjectionMessage.ReaderCoreService)]
	public partial class StartReader : Message {
		public Guid InstanceCorrelationId { get; }

		public StartReader(Guid instanceCorrelationId) {
			InstanceCorrelationId = instanceCorrelationId;
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderCoreService)]
	public partial class StopReader : Message {
		public Guid QueueId { get; }

		public StopReader(Guid queueId) {
			QueueId = queueId;
		}
	}
}
