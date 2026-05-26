// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

public sealed record WorkspaceCreated(
	string Name,
	IReadOnlyList<FilterRule> FilterRules,
	bool FullTextIndexingEnabled,
	bool SemanticIndexingEnabled,
	bool DisableMemory,
	bool DisableImports,
	bool DisableInquiries,
	bool ReadOnly,
	DateTime Timestamp);

public sealed record WorkspaceStarted(string Name, DateTime Timestamp);

public sealed record WorkspaceStopped(string Name, DateTime Timestamp);

public sealed record WorkspaceDeleted(string Name, DateTime Timestamp);