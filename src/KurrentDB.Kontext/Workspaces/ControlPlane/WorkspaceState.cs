// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using Eventuous;

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceLifecycle>))]
public enum WorkspaceLifecycle {
	Unspecified,
	Created,
	Started,
	Stopped,
	Deleted,
}

public sealed record WorkspaceState : State<WorkspaceState, WorkspaceId> {
	public IReadOnlyList<FilterRule> FilterRules { get; init; } = [];
	public bool FullTextIndexingEnabled { get; init; }
	public bool SemanticIndexingEnabled { get; init; }
	public bool DisableMemory { get; init; }
	public bool DisableImports { get; init; }
	public bool DisableInquiries { get; init; }
	public bool ReadOnly { get; init; }
	public WorkspaceLifecycle Lifecycle { get; init; }

	public WorkspaceState() {
		On<WorkspaceCreated>((s, e) => s with {
			FilterRules = e.FilterRules,
			FullTextIndexingEnabled = e.FullTextIndexingEnabled,
			SemanticIndexingEnabled = e.SemanticIndexingEnabled,
			DisableMemory = e.DisableMemory,
			DisableImports = e.DisableImports,
			DisableInquiries = e.DisableInquiries,
			ReadOnly = e.ReadOnly,
			Lifecycle = WorkspaceLifecycle.Created,
		});

		On<WorkspaceStarted>((s, _) => s with { Lifecycle = WorkspaceLifecycle.Started });
		On<WorkspaceStopped>((s, _) => s with { Lifecycle = WorkspaceLifecycle.Stopped });
		On<WorkspaceDeleted>((s, _) => s with { Lifecycle = WorkspaceLifecycle.Deleted });
	}
}