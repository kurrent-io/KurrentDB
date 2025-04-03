// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;

namespace EventStore.Projections.Core.Messages;

public interface IProjectionCheckpointManager : IHandle<CoreProjectionProcessingMessage.ReadyForCheckpoint>,
	IHandle<CoreProjectionProcessingMessage.RestartRequested>,
	IHandle<CoreProjectionProcessingMessage.Failed> {
}

public interface IEmittedStreamContainer : IProjectionCheckpointManager,
	IHandle<CoreProjectionProcessingMessage.EmittedStreamAwaiting>,
	IHandle<CoreProjectionProcessingMessage.EmittedStreamWriteCompleted> {
}
