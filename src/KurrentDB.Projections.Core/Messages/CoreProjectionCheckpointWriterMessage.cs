// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class CoreProjectionCheckpointWriterMessage {
	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public sealed partial class CheckpointWritten : Message {
		private readonly CheckpointTag _position;

		public CheckpointWritten(CheckpointTag position) {
			_position = position;
		}

		public CheckpointTag Position {
			get { return _position; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreProcessing)]
	public sealed partial class RestartRequested : Message {
		public string Reason {
			get { return _reason; }
		}

		private readonly string _reason;

		public RestartRequested(string reason) {
			_reason = reason;
		}
	}
}
