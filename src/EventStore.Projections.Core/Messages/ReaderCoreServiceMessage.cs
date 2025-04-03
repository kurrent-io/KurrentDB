// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
