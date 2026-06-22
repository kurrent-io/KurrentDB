// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Mcp.Workspace;

public class StatusToolTests {
	static readonly FakeWorkspaceContext DefaultWorkspace = new();

	static (WorkspaceRegistry registry, WorkspaceEntry entry) MakeRegistry(
		bool fts = true, bool semantic = true, WorkspaceLifecycle state = WorkspaceLifecycle.Started) {
		var entry = WorkspaceEntry.Create(WorkspaceNaming.DefaultName, [], fts, semantic,
			disableMemory: false, disableImports: false, disableInquiries: false, readOnly: false);
		entry.Status = state;
		var reg = new WorkspaceRegistry();
		reg.Upsert(entry);
		return (reg, entry);
	}

	static Task<StatusTool.StatusResult> GetStatus(FakeSystemClient client, WorkspaceRegistry registry) =>
		StatusTool.GetStatus(client, registry, DefaultWorkspace);

	[Test]
	public async Task GetStatus_CaughtUp_When_Both_Pipelines_At_Head() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, entry) = MakeRegistry();
		entry.IndexingStatus.UpdateFtsPosition(1000);
		entry.IndexingStatus.UpdateEmbeddingPosition(1000);

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.HeadPosition).IsEqualTo(1000ul);
		await Assert.That(result.FullTextIndexing.Position).IsEqualTo(1000ul);
	}

	[Test]
	public async Task GetStatus_Fts_CaughtUp_Embeddings_Behind() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, entry) = MakeRegistry();
		entry.IndexingStatus.UpdateFtsPosition(1000);
		entry.IndexingStatus.UpdateEmbeddingPosition(500);

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.CatchingUp);
		await Assert.That(result.SemanticIndexing.Position).IsEqualTo(500ul);
		await Assert.That(result.SemanticIndexing.Progress).IsEqualTo(50);
		await Assert.That(result.SemanticIndexing.BytesRemaining).IsEqualTo(500ul);
	}

	[Test]
	public async Task GetStatus_CaughtUp_When_Log_Is_Empty() {
		var client = new FakeSystemClient().SetHeadPosition(null);
		var (registry, _) = MakeRegistry();

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.HeadPosition).IsEqualTo(0ul);
	}

	[Test]
	public async Task GetStatus_CaughtUp_When_Caught_Up_Recently() {
		var client = new FakeSystemClient().SetHeadPosition(2000);
		var (registry, entry) = MakeRegistry();
		entry.IndexingStatus.UpdateFtsPosition(1000);
		entry.IndexingStatus.RecordFtsCaughtUp();
		entry.IndexingStatus.UpdateEmbeddingPosition(1000);
		entry.IndexingStatus.RecordEmbeddingCaughtUp();

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
	}

	[Test]
	public async Task GetStatus_Embeddings_Disabled_Shows_Disabled() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, entry) = MakeRegistry(semantic: false);
		entry.IndexingStatus.UpdateFtsPosition(1000);

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.CaughtUp);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.Disabled);
	}

	[Test]
	public async Task GetStatus_Reports_Workspace_State() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, _) = MakeRegistry(state: WorkspaceLifecycle.Stopped);

		var result = await GetStatus(client, registry);

		await Assert.That(result.State).IsEqualTo(nameof(WorkspaceLifecycle.Stopped));
	}

	[Test]
	public async Task GetStatus_Created_Workspace_Reported_As_Stopped() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, _) = MakeRegistry(state: WorkspaceLifecycle.Created);

		var result = await GetStatus(client, registry);

		await Assert.That(result.State).IsEqualTo(nameof(WorkspaceLifecycle.Stopped));
		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.Stopped);
	}

	[Test]
	public async Task GetStatus_Stopped_Workspace_Shows_Stopped_Not_CatchingUp() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, entry) = MakeRegistry(state: WorkspaceLifecycle.Stopped);
		entry.IndexingStatus.UpdateFtsPosition(400);
		entry.IndexingStatus.UpdateEmbeddingPosition(400);

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.Stopped);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.Stopped);
		// Last-known position is still surfaced, but progress/bytesRemaining are not (it isn't progressing).
		await Assert.That(result.FullTextIndexing.Position).IsEqualTo(400ul);
		await Assert.That(result.FullTextIndexing.Progress).IsNull();
		await Assert.That(result.FullTextIndexing.BytesRemaining).IsNull();
	}

	[Test]
	public async Task GetStatus_Disabled_Takes_Precedence_Over_Stopped() {
		var client = new FakeSystemClient().SetHeadPosition(1000);
		var (registry, _) = MakeRegistry(semantic: false, state: WorkspaceLifecycle.Stopped);

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.State).IsEqualTo(StatusTool.PipelineState.Stopped);
		await Assert.That(result.SemanticIndexing.State).IsEqualTo(StatusTool.PipelineState.Disabled);
	}

	[Test]
	public async Task GetStatus_Bytes_Remaining_Is_Raw_Number() {
		var headPos = 1_073_741_824ul + 500_000_000ul;
		var client = new FakeSystemClient().SetHeadPosition(headPos);
		var (registry, entry) = MakeRegistry();
		entry.IndexingStatus.UpdateFtsPosition(0);

		var result = await GetStatus(client, registry);

		await Assert.That(result.FullTextIndexing.BytesRemaining).IsEqualTo(headPos);
	}
}