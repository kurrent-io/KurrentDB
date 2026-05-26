// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Workspaces;

/// <summary>
/// Tests for the catch-up collapse logic in WorkspaceProjection — the optimization
/// that folds many events per workspace into a single final state during catch-up,
/// so the registry is populated in one pass at the caught-up signal.
/// </summary>
public class WorkspaceProjectionTests {
	static readonly JsonSerializerOptions Json = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
	};

	static ReadOnlyMemory<byte> Serialize<T>(T value) =>
		JsonSerializer.SerializeToUtf8Bytes(value, Json);

	static readonly string CreatedType = "$" + nameof(WorkspaceCreated);
	static readonly string StartedType = "$" + nameof(WorkspaceStarted);
	static readonly string StoppedType = "$" + nameof(WorkspaceStopped);
	static readonly string DeletedType = "$" + nameof(WorkspaceDeleted);

	static WorkspaceCreated MakeCreated(
		string name = "alpha",
		bool readOnly = false,
		IReadOnlyList<FilterRule>? rules = null) =>
		new(name,
			rules ?? [new FilterRule("orders-", null)],
			FullTextIndexingEnabled: true,
			SemanticIndexingEnabled: true,
			DisableMemory: false,
			DisableImports: false,
			DisableInquiries: false,
			ReadOnly: readOnly,
			Timestamp: DateTime.UtcNow);

	[Test]
	public async Task Accumulate_Created_Registers_PendingEntry() {
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated()));
		await Assert.That(pending.ContainsKey("alpha")).IsTrue();
		await Assert.That(pending["alpha"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Created);
		await Assert.That(pending["alpha"].FilterRules[0].StreamPrefix).IsEqualTo("orders-");
	}

	[Test]
	public async Task Accumulate_Created_Then_Started_Final_Lifecycle_Is_Started() {
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated()));
		WorkspaceProjection.Accumulate(pending, StartedType, Serialize(new WorkspaceStarted("alpha", DateTime.UtcNow)));
		await Assert.That(pending["alpha"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Started);
	}

	[Test]
	public async Task Accumulate_Created_Then_Started_Then_Stopped_Final_Lifecycle_Is_Stopped() {
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated()));
		WorkspaceProjection.Accumulate(pending, StartedType, Serialize(new WorkspaceStarted("alpha", DateTime.UtcNow)));
		WorkspaceProjection.Accumulate(pending, StoppedType, Serialize(new WorkspaceStopped("alpha", DateTime.UtcNow)));
		await Assert.That(pending["alpha"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Stopped);
	}

	[Test]
	public async Task Accumulate_Deleted_Final_Lifecycle_Is_Deleted() {
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated()));
		WorkspaceProjection.Accumulate(pending, DeletedType, Serialize(new WorkspaceDeleted("alpha", DateTime.UtcNow)));
		await Assert.That(pending["alpha"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Deleted);
	}

	[Test]
	public async Task Accumulate_Created_After_Deleted_Replaces_Pending_State() {
		// Sequence: Create → Delete → Create (with new flags). Final state should be
		// the freshly-created entry, not the deleted one.
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated()));
		WorkspaceProjection.Accumulate(pending, DeletedType, Serialize(new WorkspaceDeleted("alpha", DateTime.UtcNow)));
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated(readOnly: true)));
		await Assert.That(pending["alpha"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Created);
		await Assert.That(pending["alpha"].ReadOnly).IsTrue();
	}

	[Test]
	public async Task Accumulate_Tracks_Multiple_Workspaces_Independently() {
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated(name: "alpha")));
		WorkspaceProjection.Accumulate(pending, CreatedType, Serialize(MakeCreated(name: "beta")));
		WorkspaceProjection.Accumulate(pending, StartedType, Serialize(new WorkspaceStarted("alpha", DateTime.UtcNow)));
		WorkspaceProjection.Accumulate(pending, DeletedType, Serialize(new WorkspaceDeleted("beta", DateTime.UtcNow)));
		await Assert.That(pending["alpha"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Started);
		await Assert.That(pending["beta"].Lifecycle).IsEqualTo(WorkspaceLifecycle.Deleted);
	}

	[Test]
	public async Task Accumulate_Lifecycle_Event_For_Untracked_Workspace_Is_NoOp() {
		// Started/Stopped/Deleted before Created should be ignored — the catch-up
		// assumption is that Created always comes first.
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, StartedType, Serialize(new WorkspaceStarted("ghost", DateTime.UtcNow)));
		WorkspaceProjection.Accumulate(pending, StoppedType, Serialize(new WorkspaceStopped("ghost", DateTime.UtcNow)));
		WorkspaceProjection.Accumulate(pending, DeletedType, Serialize(new WorkspaceDeleted("ghost", DateTime.UtcNow)));
		await Assert.That(pending.ContainsKey("ghost")).IsFalse();
	}

	[Test]
	public async Task Accumulate_Ignores_Unknown_Event_Type() {
		var pending = new Dictionary<string, WorkspaceProjection.PendingState>(StringComparer.Ordinal);
		WorkspaceProjection.Accumulate(pending, "$OtherEvent", Serialize(MakeCreated()));
		await Assert.That(pending.Count).IsEqualTo(0);
	}
}