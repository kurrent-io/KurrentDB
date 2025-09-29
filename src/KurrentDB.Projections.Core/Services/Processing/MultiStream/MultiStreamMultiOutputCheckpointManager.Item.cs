// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.MultiStream;

public partial class MultiStreamMultiOutputCheckpointManager {
	private class Item(CheckpointTag tag) {
		internal KurrentDB.Core.Data.ResolvedEvent? _result;

		public CheckpointTag Tag { get; } = tag;

		public void SetLoadedEvent(KurrentDB.Core.Data.ResolvedEvent eventLinkPair) {
			_result = eventLinkPair;
		}
	}
}
