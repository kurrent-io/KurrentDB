// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Fakes;

public sealed class FakeWorkspaceContext(string workspace = WorkspaceNaming.DefaultName)
	: WorkspaceContext(httpContextAccessor: null) {
	public override string Current { get; } = workspace;
}

/// <summary>Builds a <see cref="WorkspaceRegistry"/> pre-populated with a single entry — what most tool tests need.</summary>
public static class TestWorkspace {
	public static WorkspaceRegistry Registry(
		string name = WorkspaceNaming.DefaultName,
		bool disableMemory = false,
		bool disableImports = false,
		bool disableInquiries = false,
		bool readOnly = false,
		IReadOnlyList<FilterRule>? filterRules = null) {
		var registry = new WorkspaceRegistry();
		registry.Upsert(WorkspaceEntry.Create(
			name,
			filterRules ?? [new FilterRule("", null)],
			fullTextIndexingEnabled: true,
			semanticIndexingEnabled: true,
			disableMemory: disableMemory,
			disableImports: disableImports,
			disableInquiries: disableInquiries,
			readOnly: readOnly));
		return registry;
	}
}