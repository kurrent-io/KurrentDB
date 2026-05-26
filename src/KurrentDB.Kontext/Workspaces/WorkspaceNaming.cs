// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Workspaces;

public static class WorkspaceNaming {
	public const string Category = "$kontext-workspaces:";

	public const string ManagementAllStream = "$kontext-workspaces";

	public const string DefaultName = "default";

	public const string MemoryRootPrefix = "$kontext-memory:";

	public static string ManagementStreamName(string workspaceName) => $"{Category}{workspaceName}";

	public static bool IsDefault(string workspace) =>
		string.Equals(workspace, DefaultName, StringComparison.Ordinal);

	public static string MemoryStreamPrefix(string workspaceName) => $"{MemoryRootPrefix}{workspaceName}:";

	public static string MemoryStreamName(string workspaceName, string topic) => $"{MemoryRootPrefix}{workspaceName}:{topic}";

	public static bool TryParseMemoryStream(string streamName, string workspaceName, out string topic) {
		var prefix = MemoryStreamPrefix(workspaceName);
		if (streamName.StartsWith(prefix, StringComparison.Ordinal)) {
			topic = streamName[prefix.Length..];
			return topic.Length > 0;
		}
		topic = "";
		return false;
	}

	public static string PathSegment(this IndexKind kind) => kind switch {
		IndexKind.Events => "events",
		IndexKind.Memory => "memory",
		IndexKind.EventsStreams => "events.streams",
		IndexKind.MemoryStreams => "memory.streams",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};
}