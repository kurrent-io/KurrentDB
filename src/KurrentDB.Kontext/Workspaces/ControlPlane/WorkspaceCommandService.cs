// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

public sealed class WorkspaceCommandService : CommandService<Workspace, WorkspaceState, WorkspaceId> {
	public WorkspaceCommandService(WorkspaceEventStore store, WorkspaceStreamNameMap streamNameMap)
		: base(store: store, streamNameMap: streamNameMap) {
		On<CreateWorkspaceRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new WorkspaceId(cmd.Name))
			.Act((x, cmd) => x.Create(cmd));

		On<StartWorkspaceRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new WorkspaceId(cmd.Name))
			.Act((x, _) => x.Start());

		On<StopWorkspaceRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new WorkspaceId(cmd.Name))
			.Act((x, _) => x.Stop());

		On<DeleteWorkspaceRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new WorkspaceId(cmd.Name))
			.Act((x, _) => x.Delete());
	}
}