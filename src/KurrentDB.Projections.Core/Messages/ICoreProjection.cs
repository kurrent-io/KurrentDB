// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;

namespace KurrentDB.Projections.Core.Messages;

public interface ICoreProjection :
	IHandle<CoreProjectionProcessingMessage.CheckpointLoaded>,
	IHandle<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>,
	IHandle<CoreProjectionProcessingMessage.CheckpointCompleted>,
	IHandle<CoreProjectionProcessingMessage.RestartRequested>,
	IHandle<CoreProjectionProcessingMessage.Failed> {
}
