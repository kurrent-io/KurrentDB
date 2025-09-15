// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using static KurrentDB.Core.Services.SystemStreams;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal static class EventTypeIndex {
	public static string Name(string eventType) => $"{EventTypeSecondaryIndexPrefix}{eventType}";

	private static readonly int PrefixLength = EventTypeSecondaryIndexPrefix.Length;

	public static bool TryParseEventType(string indexName, [NotNullWhen(true)] out string? eventTypeName) {
		if (!IsEventTypeIndex(indexName)) {
			eventTypeName = null;
			return false;
		}

		eventTypeName = indexName[PrefixLength..];
		return true;
	}

	public static bool IsEventTypeIndex(string indexName) => indexName.StartsWith(EventTypeSecondaryIndexPrefix);
}
