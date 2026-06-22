// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace KurrentDB.Components.Tests;

// The bridge that lets [Authorize(Policy = ...)] gate on a KurrentDB Operation: the requirement is satisfied
// iff IAuthorizationProvider.CheckAccessAsync grants the operation. This is what makes the page-level policies
// (UiPolicies) enforce the configured policy rather than a hard-coded role.
public class OperationAuthorizationHandlerTests {
	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));
	static readonly OperationRequirement Requirement = new(new Operation(Operations.Node.Information.ReadLogs));

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Succeeds_iff_the_operation_is_granted(bool grant) {
		var handler = new OperationAuthorizationHandler(new RecordingAuthorizationProvider(grant));
		var context = new AuthorizationHandlerContext([Requirement], SomeUser, resource: null);

		await handler.HandleAsync(context);

		Assert.Equal(grant, context.HasSucceeded);
	}
}
