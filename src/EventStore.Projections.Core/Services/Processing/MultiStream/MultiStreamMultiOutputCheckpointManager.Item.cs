// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.MultiStream;

public partial class MultiStreamMultiOutputCheckpointManager {
	private class Item {
		internal EventStore.Core.Data.ResolvedEvent? _result;
		private readonly CheckpointTag _tag;

		public Item(CheckpointTag tag) {
			_tag = tag;
		}

		public CheckpointTag Tag {
			get { return _tag; }
		}

		public void SetLoadedEvent(EventStore.Core.Data.ResolvedEvent eventLinkPair) {
			_result = eventLinkPair;
		}
	}
}
