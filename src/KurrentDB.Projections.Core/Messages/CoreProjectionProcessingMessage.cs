// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class CoreProjectionProcessingMessage {
	[DerivedMessage]
	public abstract partial class Message(Guid projectionId) : KurrentDB.Core.Messaging.Message {
		public Guid ProjectionId { get; } = projectionId;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class CheckpointLoaded(Guid projectionId, CheckpointTag checkpointTag, string checkpointData, long checkpointEventNumber)
		: Message(projectionId) {
		public CheckpointTag CheckpointTag { get; } = checkpointTag;
		public string CheckpointData { get; } = checkpointData;
		public long CheckpointEventNumber { get; } = checkpointEventNumber;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class PrerecordedEventsLoaded(Guid projectionId, CheckpointTag checkpointTag) : Message(projectionId) {
		public CheckpointTag CheckpointTag { get; } = checkpointTag;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class CheckpointCompleted(Guid projectionId, CheckpointTag checkpointTag) : Message(projectionId) {
		public CheckpointTag CheckpointTag { get; } = checkpointTag;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class RestartRequested(Guid projectionId, string reason) : Message(projectionId) {
		public string Reason { get; } = reason;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class Failed(Guid projectionId, string reason) : Message(projectionId) {
		public string Reason { get; } = reason;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class ReadyForCheckpoint(object sender) : KurrentDB.Core.Messaging.Message {
		public object Sender { get; } = sender;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class EmittedStreamAwaiting(string streamId, IEnvelope envelope) : KurrentDB.Core.Messaging.Message {
		public string StreamId { get; } = streamId;
		public IEnvelope Envelope { get; } = envelope;
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public partial class EmittedStreamWriteCompleted(string streamId) : KurrentDB.Core.Messaging.Message {
		public string StreamId { get; } = streamId;
	}
}
