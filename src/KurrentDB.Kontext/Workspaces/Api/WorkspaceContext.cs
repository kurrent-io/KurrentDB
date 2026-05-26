// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KurrentDB.Kontext.Workspaces.Api;

public class WorkspaceContext(IHttpContextAccessor? httpContextAccessor) {
	public virtual string Current =>
		httpContextAccessor?.HttpContext?.GetRouteValue("workspace") as string
		?? WorkspaceNaming.DefaultName;
}