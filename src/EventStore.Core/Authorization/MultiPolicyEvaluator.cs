// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public class MultiPolicyEvaluator : IPolicyEvaluator {
	private static readonly AssertionInformation DeniedByDefault = new("default", "denied by default", Grant.Deny);

	private readonly IAuthorizationPolicyRegistry _registry;
	private readonly PolicyInformation _policyInfo;

	public MultiPolicyEvaluator(IAuthorizationPolicyRegistry registry) {
		_registry = registry;
		_policyInfo = new PolicyInformation("multi-policy", 1, DateTimeOffset.MinValue);
	}

	public async ValueTask<EvaluationResult>
		EvaluateAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) {
		var evaluationContext = new EvaluationContext(operation, ct);
		foreach (var policy in _registry.EffectivePolicies) {
			var policyInfo = policy.Information;
			if (policy.TryGetAssertions(operation, out var assertions)) {
				while (!assertions.IsEmpty && evaluationContext.Grant != Grant.Deny) {
					if (ct.IsCancellationRequested) break;
					var assertion = assertions.Span[0];
					assertions = assertions.Slice(1);
					await assertion.Evaluate(cp, operation, policyInfo, evaluationContext);
				}
			}

			if (evaluationContext.Grant != Grant.Unknown) {
				return evaluationContext.ToResult();
			}
		}

		evaluationContext.Add(new AssertionMatch(_policyInfo, DeniedByDefault));
		return evaluationContext.ToResult();
	}
}
