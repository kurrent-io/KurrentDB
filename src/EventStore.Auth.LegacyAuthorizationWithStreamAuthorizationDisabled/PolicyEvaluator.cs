// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

internal class PolicyEvaluator : IPolicyEvaluator {
	private static readonly AssertionInformation DeniedByDefault =
		new AssertionInformation("default", "denied by default", Grant.Deny);

	private readonly ReadOnlyPolicy _policy;
	private readonly PolicyInformation _policyInfo;

	public PolicyEvaluator(ReadOnlyPolicy policy) {
		_policy = policy;
		_policyInfo = policy.Information;
	}

	public ValueTask<EvaluationResult>
		EvaluateAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) {
		var evaluation = new EvaluationContext(operation, ct);

		if (_policy.TryGetAssertions(operation, out var assertions))
			while (!assertions.IsEmpty && evaluation.Grant != Grant.Deny) {
				if (ct.IsCancellationRequested) break;
				var assertion = assertions.Span[0];
				assertions = assertions.Slice(1);
				var evaluate = assertion.Evaluate(cp, operation, _policyInfo, evaluation);
				if (!evaluate.IsCompleted)
					return EvaluateAsync(evaluate, cp, operation, assertions, evaluation, ct);
			}

		if (evaluation.Grant == Grant.Unknown) evaluation.Add(new AssertionMatch(_policyInfo, DeniedByDefault));

		return new ValueTask<EvaluationResult>(evaluation.ToResult());
	}

	private async ValueTask<EvaluationResult> EvaluateAsync(
		ValueTask<bool> pending,
		ClaimsPrincipal cp,
		Operation operation,
		ReadOnlyMemory<IAssertion> assertions,
		EvaluationContext evaluationContext,
		CancellationToken ct) {
		do {
			if (ct.IsCancellationRequested) break;
			await pending.ConfigureAwait(false);
			if (ct.IsCancellationRequested) break;
			if (assertions.IsEmpty) break;
			pending = assertions.Span[0].Evaluate(cp, operation, _policyInfo, evaluationContext);
			assertions = assertions.Slice(1);
		} while (evaluationContext.Grant != Grant.Deny);

		if (evaluationContext.Grant == Grant.Unknown)
			evaluationContext.Add(new AssertionMatch(_policyInfo, DeniedByDefault));

		return evaluationContext.ToResult();
	}
}
