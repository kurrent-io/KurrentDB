// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

public sealed record CreateWorkspaceRequest(
	string Name,
	IReadOnlyList<FilterRule> FilterRules,
	bool FullTextIndexingEnabled = true,
	bool SemanticIndexingEnabled = true,
	bool DisableMemory = false,
	bool DisableImports = false,
	bool DisableInquiries = false,
	bool ReadOnly = false);

public sealed record StartWorkspaceRequest(string Name);

public sealed record StopWorkspaceRequest(string Name);

public sealed record DeleteWorkspaceRequest(string Name);