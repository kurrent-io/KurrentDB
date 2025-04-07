// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

public class TestCheckpointManagerMessageHandler : IProjectionCheckpointManager, IEmittedStreamContainer {
	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.ReadyForCheckpoint> HandledMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.ReadyForCheckpoint>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.RestartRequested> HandledRestartRequestedMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.RestartRequested>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.Failed> HandledFailedMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.Failed>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.EmittedStreamWriteCompleted> HandledWriteCompletedMessage =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.EmittedStreamWriteCompleted>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.EmittedStreamAwaiting> HandledStreamAwaitingMessage =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.EmittedStreamAwaiting>();

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.ReadyForCheckpoint message) {
		HandledMessages.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.RestartRequested message) {
		HandledRestartRequestedMessages.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.Failed message) {
		HandledFailedMessages.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.EmittedStreamAwaiting message) {
		HandledStreamAwaitingMessage.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.EmittedStreamWriteCompleted message) {
		HandledWriteCompletedMessage.Add(message);
	}
}
