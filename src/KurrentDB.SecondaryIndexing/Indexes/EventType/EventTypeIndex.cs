// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Services;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

static class EventTypeIndex {
	public static string Name(string eventType) => $"{SystemStreams.EventTypeSecondaryIndexPrefix}{eventType}";

	public static bool TryParseEventType(string streamName, [NotNullWhen(true)] out string? eventTypeName) {
		if (!IsEventTypeIndex(SystemStreams.EventTypeSecondaryIndexPrefix)) {
			eventTypeName = null;
			return false;
		}

		eventTypeName = streamName[8..];
		return true;
	}

	public static bool IsEventTypeIndex(string streamName) => streamName.StartsWith(SystemStreams.EventTypeSecondaryIndexPrefix);
}
