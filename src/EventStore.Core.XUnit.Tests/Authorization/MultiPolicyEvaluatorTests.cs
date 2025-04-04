// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Plugins.Authorization;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Authorization;

public class MultiPolicyEvaluatorTests {
	private CancellationToken _ct = CancellationToken.None;
	private ClaimsPrincipal _cp = new();
	private const string TestAction = "read";

	private MultiPolicyEvaluator CreateSut(Policy[] policies) {
		var selectors = policies
			.Select(x => new StaticPolicySelector(x.AsReadOnly()))
			.ToArray<IPolicySelector>();
		return new MultiPolicyEvaluator(new StaticAuthorizationPolicyRegistry(selectors));
	}

	private Policy CreatePolicy(string policyName, TestAssertion[] assertions) {
		var policy = new Policy(policyName, 1, DateTimeOffset.MinValue);
		foreach (var assertion in assertions) {
			policy.Add(new OperationDefinition(assertion.Resource, TestAction), assertion);
		}

		return policy;
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task evaluating_a_single_policy(bool completeAsync) {
		var sut = CreateSut([CreatePolicy("test", [
			new TestAssertion("allow", Grant.Allow, completeAsync),
			new TestAssertion("deny", Grant.Deny, completeAsync),
			new TestAssertion("skip", Grant.Unknown, completeAsync)
		])]);
		await AssertGrantForResources(sut, Grant.Allow, ["allow"]);
		await AssertGrantForResources(sut, Grant.Deny, ["deny"]);
		await AssertDeniedByDefault(sut, ["unknown", "skip"]);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task evaluating_multiple_policies(bool completeAsync) {
		var policy = CreatePolicy("test", [
			new TestAssertion("allow", Grant.Allow, completeAsync),
			new TestAssertion("deny", Grant.Deny, completeAsync),
			new TestAssertion("allow-fallback", Grant.Unknown, completeAsync),
			new TestAssertion("deny-fallback", Grant.Unknown, completeAsync)
		]);
		var fallbackPolicy = CreatePolicy("fallback", [
			new TestAssertion("allow-fallback", Grant.Allow, completeAsync),
			new TestAssertion("deny-fallback", Grant.Deny, completeAsync),
			new TestAssertion("skip", Grant.Unknown, completeAsync)
		]);

		var sut = CreateSut([policy, fallbackPolicy]);
		await AssertGrantForResources(sut, Grant.Allow, ["allow", "allow-fallback"]);
		await AssertGrantForResources(sut, Grant.Deny, ["deny", "deny-fallback"]);
		await AssertDeniedByDefault(sut, ["unknown", "skip"]);
	}

	[Fact]
	public async Task evaluating_a_policy_with_no_assertions() {
		var sut = CreateSut([CreatePolicy("test", [])]);
		await AssertDeniedByDefault(sut, ["unknown"]);
	}

	[Fact]
	public async Task evaluating_with_no_policy() {
		var sut = CreateSut([]);
		await AssertDeniedByDefault(sut, ["unknown"]);
	}

	private async Task AssertDeniedByDefault(MultiPolicyEvaluator sut, string[] resources) {
		foreach (var resource in resources) {
			var result = await sut.EvaluateAsync(_cp, new Operation(resource, TestAction), _ct);
			Assert.Equal(Grant.Deny, result.Grant);
			Assert.Contains("denied by default", result.Matches.Last().Assertion.ToString());
		}
	}

	private async Task AssertGrantForResources(MultiPolicyEvaluator sut, Grant expectedGrant, string[] resources) {
		foreach (var resource in resources) {
			var result = await sut.EvaluateAsync(_cp, new Operation(resource, TestAction), _ct);
			Assert.Equal(expectedGrant, result.Grant);
			Assert.Contains(TestAssertion.Name, result.Matches.Last().Assertion.ToString());
		}
	}
}

