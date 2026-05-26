// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Workspaces.ControlPlane;

namespace KurrentDB.Kontext.Workspaces.Registry;

public sealed record WorkspaceEntry(
	string Name,
	IReadOnlyList<FilterRule> FilterRules,
	bool FullTextIndexingEnabled,
	bool SemanticIndexingEnabled,
	bool DisableMemory,
	bool DisableImports,
	bool DisableInquiries,
	bool ReadOnly,
	string MemoryStreamPrefix,
	WorkspaceIndex EventsIndex,
	WorkspaceIndex MemoryIndex,
	WorkspaceIndex EventsStreamsIndex,
	WorkspaceIndex MemoryStreamsIndex) {
	public WorkspaceLifecycle Status { get; set; } = WorkspaceLifecycle.Created;

	public IndexingStatus IndexingStatus { get; } = new();

	public WorkspaceFilterPool FilterPool { get; } = new(FilterRules);

	public static WorkspaceEntry Create(
		string name,
		IReadOnlyList<FilterRule> filterRules,
		bool fullTextIndexingEnabled,
		bool semanticIndexingEnabled,
		bool disableMemory,
		bool disableImports,
		bool disableInquiries,
		bool readOnly) =>
		new(
			Name: name,
			FilterRules: filterRules,
			FullTextIndexingEnabled: fullTextIndexingEnabled,
			SemanticIndexingEnabled: semanticIndexingEnabled,
			DisableMemory: disableMemory,
			DisableImports: disableImports,
			DisableInquiries: disableInquiries,
			ReadOnly: readOnly,
			MemoryStreamPrefix: WorkspaceNaming.MemoryStreamPrefix(name),
			EventsIndex: new WorkspaceIndex(name, IndexKind.Events),
			MemoryIndex: new WorkspaceIndex(name, IndexKind.Memory),
			EventsStreamsIndex: new WorkspaceIndex(name, IndexKind.EventsStreams),
			MemoryStreamsIndex: new WorkspaceIndex(name, IndexKind.MemoryStreams));

	public void EnsureMemoryReadable() {
		if (DisableMemory)
			throw new WorkspaceOperationDisabledException(Name, "memory read");
	}

	public void EnsureMemoryWritable() {
		if (DisableMemory)
			throw new WorkspaceOperationDisabledException(Name, "memory write");
		if (ReadOnly)
			throw new WorkspaceOperationDisabledException(Name, "memory write (workspace is read-only)");
	}

	public void EnsureImportable() {
		if (DisableImports)
			throw new WorkspaceOperationDisabledException(Name, "import");
		if (ReadOnly)
			throw new WorkspaceOperationDisabledException(Name, "import (workspace is read-only)");
	}

	public void EnsureInquiriesEnabled() {
		if (DisableInquiries)
			throw new WorkspaceOperationDisabledException(Name, "inquiries");
	}

	public bool IndexesStream(string streamName) {
		foreach (var rule in FilterRules)
			if (streamName.StartsWith(rule.StreamPrefix, StringComparison.Ordinal))
				return true;
		return false;
	}
}
