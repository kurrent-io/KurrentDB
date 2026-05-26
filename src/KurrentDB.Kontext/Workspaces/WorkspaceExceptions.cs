// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Mcp;
using KurrentDB.Kontext.Workspaces.ControlPlane;

namespace KurrentDB.Kontext.Workspaces;

public abstract class WorkspaceException(string message) : ClientFacingException(message);

public sealed class WorkspaceNotFoundException(string name)
	: WorkspaceException($"Workspace '{name}' does not exist.") {
	public string Name => name;
}

public sealed class WorkspaceAlreadyExistsException(string name)
	: WorkspaceException($"Workspace '{name}' already exists.") {
	public string Name => name;
}

public sealed class WorkspaceInvalidException(string message) : WorkspaceException(message);

public sealed class WorkspaceNotRunningException(string name)
	: WorkspaceException($"Workspace '{name}' is not started.") {
	public string Name => name;
}

public sealed class WorkspaceInvalidStateException(string name, WorkspaceLifecycle state, string action)
	: WorkspaceException($"Workspace '{name}' cannot {action} from state {state}.") {
	public string Name => name;
	public WorkspaceLifecycle State => state;
}

public sealed class WorkspaceOperationDisabledException(string name, string operation)
	: WorkspaceException($"Operation '{operation}' is disabled on workspace '{name}'.") {
	public string Name => name;
	public string Operation => operation;
}