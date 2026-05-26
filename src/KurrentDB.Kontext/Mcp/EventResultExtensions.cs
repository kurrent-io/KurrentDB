// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Kontext.Mcp;

public static class EventResultExtensions {
	public static EventResult ToEventResult(this ResolvedEvent resolved, string stream) {
		var record = resolved.OriginalEvent;
		return new EventResult {
			Stream = stream,
			EventNumber = record.EventNumber,
			CommitPosition = resolved.OriginalPosition!.Value.CommitPosition,
			PreparePosition = resolved.OriginalPosition!.Value.PreparePosition,
			EventType = record.EventType,
			Timestamp = record.TimeStamp,
			Data = record.IsJson ? record.Data : default,
			Metadata = !record.Metadata.IsEmpty ? record.Metadata : default,
		};
	}
}
