// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Workspaces;

public class WorkspaceRegistryTests {
	static WorkspaceEntry Entry(string name) =>
		WorkspaceEntry.Create(name, [new FilterRule("", null)],
			fullTextIndexingEnabled: true, semanticIndexingEnabled: true,
			disableMemory: false, disableImports: false, disableInquiries: false, readOnly: false);

	[Test]
	public async Task TryGet_Missing_Returns_False() {
		var registry = new WorkspaceRegistry();
		await Assert.That(registry.TryGet("ghost", out _)).IsFalse();
	}

	[Test]
	public async Task Upsert_Then_TryGet_Returns_Entry() {
		var registry = new WorkspaceRegistry();
		var entry = Entry("alpha");
		registry.Upsert(entry);
		await Assert.That(registry.TryGet("alpha", out var found)).IsTrue();
		await Assert.That(found).IsSameReferenceAs(entry);
	}

	[Test]
	public async Task Upsert_Replaces_Existing_Entry() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		var replacement = Entry("alpha");
		registry.Upsert(replacement);
		registry.TryGet("alpha", out var found);
		await Assert.That(found).IsSameReferenceAs(replacement);
	}

	[Test]
	public async Task Remove_Drops_The_Entry() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		registry.Remove("alpha");
		await Assert.That(registry.TryGet("alpha", out _)).IsFalse();
	}

	[Test]
	public async Task Remove_Missing_Is_NoOp() {
		var registry = new WorkspaceRegistry();
		registry.Remove("ghost");
		await Assert.That(registry.All.Count).IsEqualTo(0);
	}

	[Test]
	public async Task All_Returns_Every_Entry() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("beta"));
		registry.Upsert(Entry("alpha"));
		registry.Upsert(Entry("gamma"));
		var names = registry.All.Select(e => e.Name).OrderBy(s => s).ToArray();
		await Assert.That(names).IsEquivalentTo(new[] { "alpha", "beta", "gamma" });
	}

	[Test]
	public async Task Remove_Reflected_In_All() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		registry.Upsert(Entry("beta"));
		registry.Remove("alpha");
		var names = registry.All.Select(e => e.Name).ToArray();
		await Assert.That(names).IsEquivalentTo(new[] { "beta" });
	}

	[Test]
	public async Task SetStatus_Visible_Through_All() {
		// The All snapshot holds references to the live entries (not copies), so an in-place
		// status change is observable through it without rebuilding the snapshot.
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		var snapshot = registry.All;
		registry.SetStatus("alpha", WorkspaceLifecycle.Started);
		await Assert.That(snapshot[0].Status).IsEqualTo(WorkspaceLifecycle.Started);
	}

	[Test]
	public async Task TryGetRunning_False_Before_Status_Set() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		await Assert.That(registry.TryGetRunning("alpha", out _)).IsFalse();
	}

	[Test]
	public async Task TryGetRunning_True_When_Status_Started() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		registry.SetStatus("alpha", WorkspaceLifecycle.Started);
		await Assert.That(registry.TryGetRunning("alpha", out var entry)).IsTrue();
		await Assert.That(entry.Status).IsEqualTo(WorkspaceLifecycle.Started);
	}

	[Test]
	public async Task SetStatus_Updates_Lifecycle() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		registry.SetStatus("alpha", WorkspaceLifecycle.Stopped);
		registry.TryGet("alpha", out var entry);
		await Assert.That(entry.Status).IsEqualTo(WorkspaceLifecycle.Stopped);
	}

	[Test]
	public async Task SetStatus_Missing_Is_NoOp() {
		var registry = new WorkspaceRegistry();
		registry.SetStatus("ghost", WorkspaceLifecycle.Started);
		await Assert.That(registry.TryGet("ghost", out _)).IsFalse();
	}

	[Test]
	public async Task TryGet_Is_Case_Sensitive() {
		var registry = new WorkspaceRegistry();
		registry.Upsert(Entry("alpha"));
		await Assert.That(registry.TryGet("Alpha", out _)).IsFalse();
	}
}