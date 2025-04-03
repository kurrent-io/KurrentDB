// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using Serilog;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

internal class LegacyAuthorizationWithStreamAuthorizationDisabledProvider : AuthorizationProviderBase {
	private static readonly Stopwatch sw = Stopwatch.StartNew();
	private readonly bool _logAuthorization;
	private readonly ILogger _logger;
	private readonly bool _logSuccesses;
	private readonly IPolicyEvaluator _policyEvaluator;

	public LegacyAuthorizationWithStreamAuthorizationDisabledProvider(IPolicyEvaluator policyEvaluator,
		ILogger logger, bool logAuthorization,
		bool logSuccesses)
		: base(
			name: "LegacyAuthorizationWithStreamAuthorizationDisabled",
			requiredEntitlements: ["LEGACY_AUTHORIZATION_WITH_STREAM_AUTHORIZATION_DISABLED"]) {

		_policyEvaluator = policyEvaluator;
		_logger = logger;
		_logAuthorization = logAuthorization;
		_logSuccesses = logSuccesses;
	}

	public override ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) {
		cp ??= SystemAccounts.Anonymous;

		try {
			var startedAt = sw.Elapsed;
			var evaluationTask = _policyEvaluator.EvaluateAsync(cp, operation, ct);
			if (evaluationTask.IsCompleted)
				return new ValueTask<bool>(LogAndCheck(startedAt, cp, evaluationTask.Result));

			return CheckAccessAsync(startedAt, cp, evaluationTask);
		} catch (Exception ex) {
			_logger.Error(ex, "Error performing permission check for {identity}",
				cp.FindFirst(ClaimTypes.Name)?.Value ?? "unknown");
			return new ValueTask<bool>(false);
		}
	}

	private async ValueTask<bool> CheckAccessAsync(TimeSpan startedAt, ClaimsPrincipal cp,
		ValueTask<EvaluationResult> evaluationTask) {
		try {
			return LogAndCheck(startedAt, cp, await evaluationTask.ConfigureAwait(false));
		} catch (Exception ex) {
			_logger.Error(ex, "Error performing permission check for {identity}",
				cp.FindFirst(ClaimTypes.Name)?.Value ?? "unknown");
			return false;
		}
	}

	private bool LogAndCheck(TimeSpan startedAt, ClaimsPrincipal cp, EvaluationResult result) {
		if (_logAuthorization) {
			if (result.Grant == Grant.Allow && _logSuccesses)
				_logger.Information(
					"Successful authorization check for {identity} in {duration} with {evaluationResult}",
					cp.FindFirst(ClaimTypes.Name).Value, sw.Elapsed.Subtract(startedAt), result);
			else if (result.Grant != Grant.Allow)
				_logger.Warning("Failed authorization check for {identity} in {duration} with {evaluationResult}",
					cp.FindFirst(ClaimTypes.Name)?.Value ?? "(anonymous)", startedAt, result);
		}

		return result.Grant == Grant.Allow;
	}
}
