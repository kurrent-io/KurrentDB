// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public struct CommitEventRecord {
	public readonly EventRecord Event;
	public readonly long CommitPosition;

	public CommitEventRecord(EventRecord @event, long commitPosition) {
		Event = @event;
		CommitPosition = commitPosition;
	}

	public override string ToString() {
		return string.Format("CommitPosition: {0}, Event: {1}", CommitPosition, Event);
	}
}
