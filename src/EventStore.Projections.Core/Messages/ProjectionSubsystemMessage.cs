// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messages;

public static partial class ProjectionSubsystemMessage {
	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class RestartSubsystem : Message  {
		public IEnvelope ReplyEnvelope { get; }
		
		public RestartSubsystem(IEnvelope replyEnvelope) {
			ReplyEnvelope = replyEnvelope;
		}
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class InvalidSubsystemRestart : Message {
		public string SubsystemState { get; }
		public string Reason { get; }

		public InvalidSubsystemRestart(string subsystemState, string reason) {
			SubsystemState = subsystemState;
			Reason = reason;
		}
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class SubsystemRestarting : Message {
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class StartComponents : Message  {
		public Guid InstanceCorrelationId { get; }

		public StartComponents(Guid instanceCorrelationId) {
			InstanceCorrelationId = instanceCorrelationId;
		}
	}	
		
	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class ComponentStarted : Message  {
		public string ComponentName { get; }
		public Guid InstanceCorrelationId { get; }

		public ComponentStarted(string componentName, Guid instanceCorrelationId) {
			ComponentName = componentName;
			InstanceCorrelationId = instanceCorrelationId;
		}
	}	

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class StopComponents : Message  {
		public Guid InstanceCorrelationId { get; }

		public StopComponents(Guid instanceCorrelationId) {
			InstanceCorrelationId = instanceCorrelationId;
		}
	}
	
	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class ComponentStopped : Message {
		public string ComponentName { get; }
		public Guid InstanceCorrelationId { get; }

		public ComponentStopped(string componentName, Guid instanceCorrelationId) {
			ComponentName = componentName;
			InstanceCorrelationId = instanceCorrelationId;
		}
	}

	[DerivedMessage(ProjectionMessage.Subsystem)]
	public partial class IODispatcherDrained : Message {
		public string ComponentName { get; }

		public IODispatcherDrained(string componentName) {
			ComponentName = componentName;
		}
	}
}
