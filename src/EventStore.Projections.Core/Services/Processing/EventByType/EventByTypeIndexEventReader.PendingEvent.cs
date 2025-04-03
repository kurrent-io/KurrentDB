// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

namespace EventStore.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader
{
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
