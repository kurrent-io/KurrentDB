// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public class TestCheckpointManagerMessageHandler : IProjectionCheckpointManager, IEmittedStreamContainer {
	public readonly List<CoreProjectionProcessingMessage.ReadyForCheckpoint> HandledMessages =
		new List<CoreProjectionProcessingMessage.ReadyForCheckpoint>();

	public readonly List<CoreProjectionProcessingMessage.RestartRequested> HandledRestartRequestedMessages =
		new List<CoreProjectionProcessingMessage.RestartRequested>();

	public readonly List<CoreProjectionProcessingMessage.Failed> HandledFailedMessages =
		new List<CoreProjectionProcessingMessage.Failed>();

	public readonly List<CoreProjectionProcessingMessage.EmittedStreamWriteCompleted> HandledWriteCompletedMessage =
		new List<CoreProjectionProcessingMessage.EmittedStreamWriteCompleted>();

	public readonly List<CoreProjectionProcessingMessage.EmittedStreamAwaiting> HandledStreamAwaitingMessage =
		new List<CoreProjectionProcessingMessage.EmittedStreamAwaiting>();

	public void Handle(CoreProjectionProcessingMessage.ReadyForCheckpoint message) {
		HandledMessages.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.RestartRequested message) {
		HandledRestartRequestedMessages.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.Failed message) {
		HandledFailedMessages.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.EmittedStreamAwaiting message) {
		HandledStreamAwaitingMessage.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.EmittedStreamWriteCompleted message) {
		HandledWriteCompletedMessage.Add(message);
	}
}
