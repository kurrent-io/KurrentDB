// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messages;

public static partial class ProjectionCoreServiceMessage {
	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class StartCore : Message {
		public readonly Guid InstanceCorrelationId;

		public StartCore(Guid instanceCorrelationId) {
			InstanceCorrelationId = instanceCorrelationId;
		}
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class StopCore : Message {
		public Guid QueueId { get; }

		public StopCore(Guid queueId) {
			QueueId = queueId;
		}
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class StopCoreTimeout : Message {
		public Guid QueueId { get; }

		public StopCoreTimeout(Guid queueId) {
			QueueId = queueId;
		}
	}
	
	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class CoreTick : Message {
		private readonly Action _action;

		public CoreTick(Action action) {
			_action = action;
		}

		public Action Action {
			get { return _action; }
		}
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class SubComponentStarted : Message {
		public string SubComponent { get; }
		public Guid InstanceCorrelationId { get; }
	
		public SubComponentStarted(string subComponent, Guid instanceCorrelationId) {
			InstanceCorrelationId = instanceCorrelationId;
			SubComponent = subComponent;
		}
	}

	[DerivedMessage(ProjectionMessage.ServiceMessage)]
	public partial class SubComponentStopped : Message {
		public readonly string SubComponent;

		public Guid QueueId { get; }

		public SubComponentStopped(string subComponent, Guid queueId) {
			SubComponent = subComponent;
			QueueId = queueId;
		}
	}
}
