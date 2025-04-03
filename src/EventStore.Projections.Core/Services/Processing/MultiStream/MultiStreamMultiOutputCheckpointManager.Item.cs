// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.MultiStream;

public partial class MultiStreamMultiOutputCheckpointManager
{
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
