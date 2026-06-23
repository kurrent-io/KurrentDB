// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace KurrentDB.Components.Shared;

// Bridges an ASP.NET Core authorization policy to a KurrentDB Operation: a policy carrying an
// OperationRequirement is satisfied iff the configured IAuthorizationProvider grants the operation —
// the same check the gRPC/HTTP front-ends (and the UI services) perform. This lets pages gate on an
// operation via the framework's [Authorize(Policy = ...)] attribute, enforced before the page renders.
public sealed class OperationRequirement(Operation operation) : IAuthorizationRequirement {
	public Operation Operation { get; } = operation;
}

public sealed class OperationAuthorizationHandler(IAuthorizationProvider authorizationProvider)
	: AuthorizationHandler<OperationRequirement> {
	protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement) {
		if (await authorizationProvider.CheckAccessAsync(context.User, requirement.Operation, CancellationToken.None))
			context.Succeed(requirement);
	}
}
