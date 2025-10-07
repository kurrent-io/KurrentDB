// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Projections.Core.Messages;

public static partial class ProjectionSubsystemMessage {
	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class RestartSubsystem(IEnvelope replyEnvelope) : Message {
		public IEnvelope ReplyEnvelope { get; } = replyEnvelope;
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class InvalidSubsystemRestart(string subsystemState, string reason) : Message {
		public string SubsystemState { get; } = subsystemState;
		public string Reason { get; } = reason;
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class SubsystemRestarting : Message;

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class StartComponents(Guid instanceCorrelationId) : Message {
		public Guid InstanceCorrelationId { get; } = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class ComponentStarted(string componentName, Guid instanceCorrelationId) : Message {
		public string ComponentName { get; } = componentName;
		public Guid InstanceCorrelationId { get; } = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class StopComponents(Guid instanceCorrelationId) : Message {
		public Guid InstanceCorrelationId { get; } = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class ComponentStopped(string componentName, Guid instanceCorrelationId) : Message {
		public string ComponentName { get; } = componentName;
		public Guid InstanceCorrelationId { get; } = instanceCorrelationId;
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class IODispatcherDrained(string componentName) : Message {
		public string ComponentName { get; } = componentName;
	}
}
