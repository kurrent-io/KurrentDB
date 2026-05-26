// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Core;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Workspace;

[McpServerToolType]
public class StatusTool {
	[McpServerTool(Name = "ws_status", UseStructuredContent = true), Description(
		"Check the status of the current workspace — indexing progress and log head position.")]
	public static async Task<StatusResult> GetStatus(
		ISystemClient client,
		WorkspaceRegistry workspaces,
		WorkspaceContext workspaceContext,
		CancellationToken ct = default) {
		if (!workspaces.TryGet(workspaceContext.Current, out var entry))
			throw new WorkspaceNotFoundException(workspaceContext.Current);

		var head = await client.GetHeadPositionAsync(ct);
		var status = entry.IndexingStatus;
		var started = entry.Status == WorkspaceLifecycle.Started;
		return new StatusResult(
			Workspace: workspaceContext.Current,
			State: started ? nameof(WorkspaceLifecycle.Started) : nameof(WorkspaceLifecycle.Stopped),
			HeadPosition: head,
			FullTextIndexing: BuildPipelineStatus(head, status.FtsPosition, status.FtsLastCaughtUpAt,
				enabled: entry.FullTextIndexingEnabled, started: started),
			SemanticIndexing: BuildPipelineStatus(head, status.EmbeddingPosition, status.EmbeddingLastCaughtUpAt,
				enabled: entry.SemanticIndexingEnabled, started: started));
	}

	static PipelineStatus BuildPipelineStatus(ulong head, ulong position, DateTime? lastCaughtUpAt, bool enabled, bool started) {
		if (!enabled)
			return new(PipelineState.Disabled, null, null, null, null);

		if (!started)
			return new(PipelineState.Stopped, position, lastCaughtUpAt, null, null);

		if (head == 0
			|| (position > 0 && position >= head)
			|| (lastCaughtUpAt.HasValue && DateTime.UtcNow - lastCaughtUpAt.Value < TimeSpan.FromSeconds(15)))
			return new(PipelineState.CaughtUp, position, lastCaughtUpAt, null, null);

		var bytesRemaining = head - position;
		var progress = (int)(position * 100 / head);
		return new(PipelineState.CatchingUp, position, lastCaughtUpAt, progress, bytesRemaining);
	}

	public static class PipelineState {
		public const string CaughtUp = "caughtUp";
		public const string CatchingUp = "catchingUp";
		public const string Stopped = "stopped";
		public const string Disabled = "disabled";
	}

	public sealed record StatusResult(
		[property: Description("Name of the current workspace.")]
		string Workspace,
		[property: Description("Workspace state: either Started or Stopped.")]
		string State,
		[property: Description("Commit position of the latest event in the log.")]
		ulong HeadPosition,
		[property: Description("Full-text indexing pipeline status.")]
		PipelineStatus FullTextIndexing,
		[property: Description("Semantic (embeddings) indexing pipeline status.")]
		PipelineStatus SemanticIndexing);

	public sealed record PipelineStatus(
		[property: Description("One of: caughtUp, catchingUp, stopped, disabled.")]
		string State,
		[property: Description("Commit position of the latest event processed by the subscription.")]
		ulong? Position = null,
		[property: Description("UTC timestamp of the last time the subscription reached the live tail, if ever.")]
		DateTime? LastCaughtUpAt = null,
		[property: Description("Approximate percent of log bytes already indexed. Only set when catchingUp.")]
		int? Progress = null,
		[property: Description("Approximate bytes remaining between the current position and the head. Only set when catchingUp.")]
		ulong? BytesRemaining = null);
}
