// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;

namespace KurrentDB.Kontext.Tests.Workspaces;

public class WorkspaceTests {
	static IReadOnlyList<FilterRule> OneRule(string prefix = "orders-", string? filter = null) =>
		[new FilterRule(prefix, filter)];

	static CreateWorkspaceRequest CreateCmd(
		string name = "alpha",
		IReadOnlyList<FilterRule>? rules = null,
		bool fullText = true, bool semantic = true,
		bool disableMemory = false, bool disableImports = false,
		bool disableInquiries = false, bool readOnly = false) =>
		new(name, rules ?? OneRule(), fullText, semantic,
			disableMemory, disableImports, disableInquiries, readOnly);

	[Test]
	public async Task Create_NewWorkspace_Emits_WorkspaceCreated() {
		var ws = new Workspace();
		ws.Create(CreateCmd());
		var evt = ws.Changes.OfType<WorkspaceCreated>().Single();
		await Assert.That(evt.Name).IsEqualTo("alpha");
		await Assert.That(evt.FilterRules.Count).IsEqualTo(1);
		await Assert.That(evt.FullTextIndexingEnabled).IsTrue();
	}

	[Test]
	public async Task Create_Idempotent_When_Rules_And_Flags_Match() {
		var ws = new Workspace();
		ws.Create(CreateCmd());
		ws.ClearChanges();
		ws.Create(CreateCmd()); // re-create with same settings
		await Assert.That(ws.Changes.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Create_Throws_When_Rules_Differ_On_ReCreate() {
		var ws = new Workspace();
		ws.Create(CreateCmd(rules: OneRule("orders-")));
		Assert.Throws<WorkspaceAlreadyExistsException>(() =>
			ws.Create(CreateCmd(rules: OneRule("payments-"))));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Create_Throws_When_Flags_Differ_On_ReCreate() {
		var ws = new Workspace();
		ws.Create(CreateCmd());
		Assert.Throws<WorkspaceAlreadyExistsException>(() =>
			ws.Create(CreateCmd(readOnly: true)));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Create_Throws_On_Empty_Name() {
		var ws = new Workspace();
		var ex = Assert.Throws<WorkspaceInvalidException>(() => ws.Create(CreateCmd(name: "")));
		await Assert.That(ex.Message).Contains("must not be empty");
	}

	[Test]
	public async Task Create_Throws_On_Whitespace_Name() {
		var ws = new Workspace();
		Assert.Throws<WorkspaceInvalidException>(() => ws.Create(CreateCmd(name: "   ")));
		await Task.CompletedTask;
	}

	[Test]
	[Arguments("alpha bar")]
	[Arguments("alpha/bar")]
	[Arguments("alpha.bar")]
	[Arguments("alpha+bar")]
	public async Task Create_Throws_On_Disallowed_Name_Chars(string name) {
		var ws = new Workspace();
		Assert.Throws<WorkspaceInvalidException>(() => ws.Create(CreateCmd(name: name)));
		await Task.CompletedTask;
	}

	[Test]
	[Arguments("alpha_bar")]
	[Arguments("alpha-bar")]
	[Arguments("Workspace123")]
	public async Task Create_Accepts_Valid_Names(string name) {
		var ws = new Workspace();
		ws.Create(CreateCmd(name: name));
		await Assert.That(ws.Changes.OfType<WorkspaceCreated>().Single().Name).IsEqualTo(name);
	}

	[Test]
	public async Task Create_Default_Workspace_Accepts_Canonical_Rule() {
		var ws = new Workspace();
		ws.Create(CreateCmd(name: WorkspaceNaming.DefaultName, rules: [new FilterRule("", null)]));
		await Assert.That(ws.Changes.OfType<WorkspaceCreated>().Count()).IsEqualTo(1);
	}

	[Test]
	public async Task Create_Default_Workspace_Rejects_Non_Canonical_Rule() {
		var ws = new Workspace();
		Assert.Throws<WorkspaceInvalidException>(() =>
			ws.Create(CreateCmd(name: WorkspaceNaming.DefaultName, rules: [new FilterRule("orders-", null)])));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Create_Default_Workspace_Rejects_Rule_With_Filter() {
		var ws = new Workspace();
		Assert.Throws<WorkspaceInvalidException>(() =>
			ws.Create(CreateCmd(name: WorkspaceNaming.DefaultName, rules: [new FilterRule("", "event => true")])));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Create_Default_Workspace_Rejects_Multiple_Rules() {
		var ws = new Workspace();
		Assert.Throws<WorkspaceInvalidException>(() =>
			ws.Create(CreateCmd(name: WorkspaceNaming.DefaultName,
				rules: [new FilterRule("", null), new FilterRule("extra-", null)])));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Create_Throws_On_Empty_Rule_List_For_NonDefault() {
		var ws = new Workspace();
		var ex = Assert.Throws<WorkspaceInvalidException>(() =>
			ws.Create(CreateCmd(rules: [])));
		await Assert.That(ex.Message).Contains("At least one filter rule");
	}

	[Test]
	public async Task Create_Accepts_Rule_With_Valid_JS_Filter() {
		var ws = new Workspace();
		ws.Create(CreateCmd(rules: [new FilterRule("orders-", "event => event.value.userId === 123")]));
		await Assert.That(ws.Changes.OfType<WorkspaceCreated>().Count()).IsEqualTo(1);
	}

	[Test]
	public async Task Create_Throws_On_Rule_With_Invalid_JS_Filter() {
		var ws = new Workspace();
		var ex = Assert.Throws<WorkspaceInvalidException>(() =>
			ws.Create(CreateCmd(rules: [new FilterRule("orders-", "this is not js!!!")])));
		await Assert.That(ex.Message).Contains("failed to compile");
	}
}