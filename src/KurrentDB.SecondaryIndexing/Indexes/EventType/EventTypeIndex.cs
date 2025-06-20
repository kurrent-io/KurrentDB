// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Services;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal static class EventTypeIndex {
	private const string Prefix = $"{SystemStreams.IndexStreamPrefix}et-";
	public static string Name(string eventType) => $"{Prefix}{eventType}";

	public static bool TryGetEventType(string streamName, [NotNullWhen(true)] out string? eventTypeName) {
		if (!IsEventTypeIndexStream(Prefix)) {
			eventTypeName = null;
			return false;
		}

		eventTypeName = streamName[8..];
		return true;
	}

	public static bool IsEventTypeIndexStream(string streamName) =>
		streamName.StartsWith(Prefix);
}
