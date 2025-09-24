// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Projections.Core.Messages;

public static partial class ProjectionCoreServiceMessage {
	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class StartCore(Guid instanceCorrelationId) : Message {
		public readonly Guid InstanceCorrelationId = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class StopCore(Guid queueId) : Message {
		public Guid QueueId { get; } = queueId;
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class StopCoreTimeout(Guid queueId) : Message {
		public Guid QueueId { get; } = queueId;
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class CoreTick(Action action) : Message {
		public Action Action => action;
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class SubComponentStarted(string subComponent, Guid instanceCorrelationId) : Message {
		public string SubComponent { get; } = subComponent;
		public Guid InstanceCorrelationId { get; } = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class SubComponentStopped(string subComponent, Guid queueId) : Message {
		public readonly string SubComponent = subComponent;

		public Guid QueueId { get; } = queueId;
	}
}
