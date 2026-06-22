// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Mcp.Workspace;

public class ImportToolTests {
	static WorkspaceRegistry MakeRegistry(IReadOnlyList<FilterRule>? rules = null) =>
		TestWorkspace.Registry(filterRules: rules ?? [new FilterRule("", null)]);

	[Test]
	public async Task Import_Throws_Without_HttpTransport() {
		var sessions = new ActiveMcpSessions();

		await Assert.That(() => ImportTool.Import(sessions, MakeRegistry(), new FakeWorkspaceContext()))
			.Throws<ImportUnavailableException>()
			.WithMessageContaining("requires HTTP transport");
	}
}