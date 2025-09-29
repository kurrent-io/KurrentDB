// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class CoreProjectionCheckpointWriterMessage {
	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public sealed partial class CheckpointWritten(CheckpointTag position) : Message {
		public CheckpointTag Position { get; } = position;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public sealed partial class RestartRequested(string reason) : Message {
		public string Reason { get; } = reason;
	}
}
