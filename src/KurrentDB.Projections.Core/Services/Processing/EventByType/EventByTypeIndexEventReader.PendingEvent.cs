// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader {
	private class PendingEvent(KurrentDB.Core.Data.ResolvedEvent resolvedEvent, TFPos tfPosition, float progress) {
		public readonly KurrentDB.Core.Data.ResolvedEvent ResolvedEvent = resolvedEvent;
		public readonly float Progress = progress;
		public readonly TFPos TfPosition = tfPosition;
	}
}
