// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Core.Authorization;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authorization;
using Xunit;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

public class StreamPolicyAssertionTests {
	private readonly PolicyInformation _policyInfo = new("fake", 1, DateTimeOffset.MinValue);
	private const string _publicStream = "public-stream";
	private const string _restrictedStream = "restricted-stream";
	private const string _privilegedUser = "ouro";
	private const string _nonPrivilegedUser = "test-user";

	private (StreamPolicyAssertion, FakePolicyProvider) CreateSut() {
		var policyProvider = new FakePolicyProvider(_restrictedStream, _privilegedUser);
		return (new StreamPolicyAssertion(stream => policyProvider.GetAccessPolicyFor(stream)), policyProvider);
	}

	public static IEnumerable<object[]> ValidActionsData =>
		PolicyTestHelpers.ValidActions.Select(x => new[] { (object)x });

	[Fact]
	public async Task unknown_action_throws_exception() {
		var (sut, _) = CreateSut();
		var user = SystemAccounts.System;

		var operation = new Operation(new OperationDefinition("unknown", "unknown"))
			.WithParameter(Operations.Streams.Parameters.StreamId(_publicStream));
		var context = new EvaluationContext(operation, new CancellationToken());

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
			async () => await sut.Evaluate(user, operation, _policyInfo, context));
	}

	[Theory]
	[MemberData(nameof(ValidActionsData))]
	public async Task when_operation_has_no_stream_id_access_is_denied(string action) {
		var (sut, _) = CreateSut();
		var user = SystemAccounts.System;

		var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action)); // No parameters added
		var context = new EvaluationContext(operation, new CancellationToken());

		var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);
		var matchResult = context.ToResult();

		Assert.True(matchFound);
		Assert.Equal(Grant.Deny, context.Grant);
		Assert.Contains("streamId is null", matchResult.ToString());
	}

	[Theory]
	[InlineData("read", "", Grant.Allow)] // "" translates to "$all"
	[InlineData("read", "$all", Grant.Allow)]
	[InlineData("metadataWrite", "", Grant.Allow)]
	[InlineData("metadataWrite", "$all", Grant.Allow)]
	[InlineData("write", "$$$all", Grant.Allow)]
	[InlineData("write", "", Grant.Deny)]
	[InlineData("write", "$all", Grant.Deny)]
	[InlineData("delete", "", Grant.Deny)]
	[InlineData("delete", "$all", Grant.Deny)]
	public async Task actions_on_all_stream_with_system_user(string action, string stream, Grant expectedGrant) {
		var (sut, _) = CreateSut();
		var user = SystemAccounts.System;

		var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action))
			.WithParameter(Operations.Streams.Parameters.StreamId(stream));
		var context = new EvaluationContext(operation, new CancellationToken());

		var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);
		var matchResult = context.ToResult();

		Assert.True(matchFound);
		Assert.Equal(expectedGrant, context.Grant);
		if (expectedGrant is Grant.Deny)
			Assert.Contains("denied on $all", matchResult.ToString());
	}

	[Theory]
	[InlineData(_publicStream, SystemUsers.Admin)]
	[InlineData(_publicStream, "customAdmin")]
	[InlineData(_restrictedStream, SystemUsers.Admin)]
	[InlineData(_restrictedStream, "customAdmin")]
	public async Task admin_is_always_granted_access(string stream, string username) {
		var (sut, _) = CreateSut();
		var user = PolicyTestHelpers.CreateUser(username, [SystemRoles.Admins]);

		foreach (var action in PolicyTestHelpers.ValidActions) {
			var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action))
				.WithParameter(Operations.Streams.Parameters.StreamId(stream));
			var context = new EvaluationContext(operation, new CancellationToken());

			var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);

			Assert.True(matchFound);
			Assert.Equal(Grant.Allow, context.Grant);
		}
	}

	[Theory]
	[InlineData(_restrictedStream, _privilegedUser, new string[] { }, Grant.Allow)]
	[InlineData(_restrictedStream, "test-user", new[] { _privilegedUser }, Grant.Allow)]
	[InlineData(_publicStream, _privilegedUser, new string[] { }, Grant.Allow)]
	[InlineData(_publicStream, "test-user", new[] { _privilegedUser }, Grant.Allow)]
	[InlineData(_restrictedStream, "test-user", new string[] { }, Grant.Deny)]
	[InlineData(_restrictedStream, "test-user", new[] { "test-role" }, Grant.Deny)]
	[InlineData(_publicStream, "test-user", new string[] { }, Grant.Allow)]
	[InlineData(_publicStream, "test-user", new[] { "test-role" }, Grant.Allow)]
	public async Task action_on_user_streams_with_custom_user(
		string stream, string username, string[] role, Grant expectedGrant) {
		var (sut, _) = CreateSut();
		var user = PolicyTestHelpers.CreateUser(username, role);

		foreach (var action in PolicyTestHelpers.ValidActions) {
			var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action))
				.WithParameter(Operations.Streams.Parameters.StreamId(stream));
			var context = new EvaluationContext(operation, new CancellationToken());

			var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);

			Assert.True(matchFound);
			Assert.Equal(expectedGrant, context.Grant);
		}
	}

	[Theory]
	[InlineData(_restrictedStream, SystemUsers.Operations, new[] { SystemRoles.Operations }, Grant.Deny)]
	[InlineData(_restrictedStream, "customOps", new[] { SystemRoles.Operations }, Grant.Deny)]
	[InlineData(_publicStream, SystemUsers.Operations, new[] { SystemRoles.Operations }, Grant.Deny)]
	[InlineData(_publicStream, "customOps", new[] { SystemRoles.Operations }, Grant.Deny)]
	[InlineData(_publicStream, "customOps", new[] { SystemRoles.Operations, "customRole" }, Grant.Allow)]
	public async Task users_with_only_the_ops_role_are_denied_access_to_public_and_restricted_streams(
		string stream, string username, string[] roles, Grant expectedGrant) {
		var (sut, _) = CreateSut();
		var user = PolicyTestHelpers.CreateUser(username, roles);

		foreach (var action in PolicyTestHelpers.ValidActions) {
			var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action))
				.WithParameter(Operations.Streams.Parameters.StreamId(stream));
			var context = new EvaluationContext(operation, new CancellationToken());

			var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);

			Assert.True(matchFound);
			Assert.Equal(expectedGrant, context.Grant);
		}
	}

	[Theory]
	[InlineData("read", "custom-stream", Grant.Deny)]
	[InlineData("write", "custom-stream", Grant.Deny)]
	[InlineData("delete", "custom-stream", Grant.Deny)]
	[InlineData("delete", "$$custom-stream", Grant.Deny)]
	[InlineData("metadataRead", "custom-stream", Grant.Allow)]
	[InlineData("metadataWrite", "custom-stream", Grant.Allow)]
	[InlineData("read", "$$custom-stream", Grant.Allow)]
	[InlineData("write", "$$custom-stream", Grant.Allow)]
	[InlineData("metadataRead", "$$custom-stream", Grant.Deny)]
	[InlineData("metadataWrite", "$$custom-stream", Grant.Deny)]
	public async Task user_with_meta_access_only_does_not_have_normal_access(
		string attemptedAction, string stream, Grant expectedGrant) {
		var (sut, policyService) = CreateSut();
		var user = PolicyTestHelpers.CreateUser(_nonPrivilegedUser, []);
		var accessPolicy = new AccessPolicy([], [], [], [_nonPrivilegedUser], [_nonPrivilegedUser]);
		policyService.SetCustomAccessPolicy(stream, accessPolicy);

		var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(attemptedAction))
			.WithParameter(Operations.Streams.Parameters.StreamId(stream));
		var context = new EvaluationContext(operation, new CancellationToken());

		var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);

		Assert.True(matchFound);
		Assert.Equal(expectedGrant, context.Grant);
	}

	[Theory]
	[MemberData(nameof(ValidActionsData))]
	public async Task user_is_restricted_to_allowed_actions(string allowedAction) {
		var (sut, policyService) = CreateSut();
		string stream = "custom-stream";
		var user = PolicyTestHelpers.CreateUser(_nonPrivilegedUser, []);
		var accessPolicy = PolicyTestHelpers.CreateAccessPolicyForAction(allowedAction, _nonPrivilegedUser);

		policyService.SetCustomAccessPolicy(stream, accessPolicy);

		foreach (var action in PolicyTestHelpers.ValidActions) {
			var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action))
				.WithParameter(Operations.Streams.Parameters.StreamId(stream));
			var context = new EvaluationContext(operation, new CancellationToken());

			var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);

			Assert.True(matchFound);
			Assert.Equal(action == allowedAction ? Grant.Allow : Grant.Deny, context.Grant);
		}
	}

	public static IEnumerable<object[]> UnauthenticatedUsers =>
		new List<object[]> {
			new object[] { new ClaimsPrincipal() }, // No identity
			new object[] { // Admin claim, but authentication type is not set
				new ClaimsPrincipal(new[] {
					new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, SystemRoles.Admins) }, "")
				})
			},
			new object[] { // Anonymous claim
				new ClaimsPrincipal(
					new ClaimsIdentity(new[] { new Claim(ClaimTypes.Anonymous, "") }))
			}
		};

	[Theory]
	[MemberData(nameof(UnauthenticatedUsers))]
	public async Task unauthenticated_users_are_denied_access(ClaimsPrincipal user) {
		var (sut, _) = CreateSut();
		Assert.False(user.Identity?.IsAuthenticated ?? false);

		foreach (var action in PolicyTestHelpers.ValidActions) {
			var operation = new Operation(PolicyTestHelpers.ActionToOperationDefinition(action))
				.WithParameter(Operations.Streams.Parameters.StreamId(_publicStream));
			var context = new EvaluationContext(operation, new CancellationToken());

			var matchFound = await sut.Evaluate(user, operation, _policyInfo, context);

			Assert.True(matchFound);
			Assert.Equal(Grant.Deny, context.Grant);
		}
	}
}
