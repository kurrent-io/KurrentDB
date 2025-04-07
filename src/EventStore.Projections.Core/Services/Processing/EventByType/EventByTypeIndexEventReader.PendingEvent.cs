// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Data;

namespace EventStore.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader {
	private class PendingEvent {
		public readonly EventStore.Core.Data.ResolvedEvent ResolvedEvent;
		public readonly float Progress;
		public readonly TFPos TfPosition;

		public PendingEvent(EventStore.Core.Data.ResolvedEvent resolvedEvent, TFPos tfPosition, float progress) {
			ResolvedEvent = resolvedEvent;
			Progress = progress;
			TfPosition = tfPosition;
		}
	}
}
