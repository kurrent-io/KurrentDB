// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces.ControlPlane;

namespace KurrentDB.Kontext.Tests.Mcp.Workspace;

public class ManagementToolTests {
	[Test]
	public async Task Manage_Throws_Without_HttpTransport() {
		var sessions = new ActiveMcpSessions();
		var registry = TestWorkspace.Registry(filterRules: [new FilterRule("", null)]);

		await Assert.That(() => ManagementTool.Manage(sessions, registry, new FakeWorkspaceContext()))
			.Throws<ManagementUnavailableException>()
			.WithMessageContaining("requires HTTP transport");
	}

	[Test]
	public async Task BuildInstructions_Reports_Current_Workspace_And_State() {
		var started = ManagementTool.BuildInstructions("https://localhost:2113", "alpha", started: true);
		await Assert.That(started.CurrentWorkspace).IsEqualTo("alpha");
		await Assert.That(started.CurrentWorkspaceState).IsEqualTo("Started");

		var stopped = ManagementTool.BuildInstructions("https://localhost:2113", "alpha", started: false);
		await Assert.That(stopped.CurrentWorkspaceState).IsEqualTo("Stopped");
	}

	[Test]
	public async Task BuildInstructions_Commands_Target_The_Api_And_Current_Workspace() {
		var r = ManagementTool.BuildInstructions("https://localhost:2113/", "alpha", started: false);

		// Trailing slash on the base URL is trimmed before the control-plane path is appended (no double slash).
		await Assert.That(r.Commands["list"]).Contains("localhost:2113/kontext/workspaces");
		await Assert.That(r.Commands["view"]).Contains("/kontext/workspaces/alpha");
		await Assert.That(r.Commands["start"]).Contains("/kontext/workspaces/alpha/start");
		await Assert.That(r.Commands["stop"]).Contains("/kontext/workspaces/alpha/stop");
		await Assert.That(r.Commands["delete"]).Contains("--request DELETE");
		// create carries the filterRules payload template with the placeholders the agent fills in.
		await Assert.That(r.Commands["create"]).Contains("--request POST");
		await Assert.That(r.Commands["create"]).Contains("<new-workspace>");
		await Assert.That(r.Commands["create"]).Contains("<stream-prefix>");
		await Assert.That(r.Commands["create"]).Contains("<optional-js-filter>");
		// Every command carries the default admin credentials.
		await Assert.That(r.Commands["start"]).Contains("admin:changeit");
	}
}
