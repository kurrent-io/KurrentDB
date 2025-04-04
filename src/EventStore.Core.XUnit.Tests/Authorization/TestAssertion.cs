// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Core.Authorization;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.XUnit.Tests.Authorization;

public class TestAssertion : IAssertion {
	public Grant Grant => Grant.Deny;
	public const string Name = "TestAssertion";

	public readonly string Resource;
	private readonly Grant _resultGrant;
	private readonly bool _evaluateAsynchronously;
	public AssertionInformation Information { get; } = new("test", "unknown", Grant.Unknown);
	public TestAssertion(string resource, Grant grant, bool evaluateAsynchronously) {
		Resource = resource;
		_resultGrant = grant;
		_evaluateAsynchronously = evaluateAsynchronously;
	}

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy, EvaluationContext context) {
		return _evaluateAsynchronously
			? EvaluateAsync(operation.Resource, policy, context)
			: EvaluateSync(operation.Resource, policy, context);
	}

	private ValueTask<bool> EvaluateSync(string resource, PolicyInformation policy, EvaluationContext context) {
		if (resource == Resource) {
			context.Add(new AssertionMatch(policy, new AssertionInformation(Name, Resource, _resultGrant)));
			return new ValueTask<bool>(true);
		}
		context.Add(new AssertionMatch(policy, new AssertionInformation(Name, "unknown", Grant.Unknown)));
		return new ValueTask<bool>(false);
	}

	private async ValueTask<bool> EvaluateAsync(string resource, PolicyInformation policy, EvaluationContext context) {
		await Task.Delay(500);
		return await EvaluateSync(resource, policy, context);
	}
}
