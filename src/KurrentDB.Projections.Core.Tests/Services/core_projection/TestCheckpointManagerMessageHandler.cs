// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

public class TestCheckpointManagerMessageHandler : IEmittedStreamContainer {
	public readonly List<CoreProjectionProcessingMessage.ReadyForCheckpoint> HandledMessages = [];
	public readonly List<CoreProjectionProcessingMessage.RestartRequested> HandledRestartRequestedMessages = [];
	public readonly List<CoreProjectionProcessingMessage.Failed> HandledFailedMessages = [];
	public readonly List<CoreProjectionProcessingMessage.EmittedStreamWriteCompleted> HandledWriteCompletedMessage = [];
	public readonly List<CoreProjectionProcessingMessage.EmittedStreamAwaiting> HandledStreamAwaitingMessage = [];

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
