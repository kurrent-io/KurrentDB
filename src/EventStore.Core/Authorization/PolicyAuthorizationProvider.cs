// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using Serilog;

namespace EventStore.Core.Authorization;

public class PolicyAuthorizationProvider(IPolicyEvaluator policyEvaluator, bool logAuthorization = true, bool logSuccesses = false) : AuthorizationProviderBase {
	static readonly ILogger Logger = Log.ForContext<PolicyAuthorizationProvider>();
	static readonly TimeProvider Time = TimeProvider.System;

	bool LogAccessDenied  => logAuthorization;
	bool LogAccessGranted => LogAccessDenied && logSuccesses;

	public override ValueTask<bool> CheckAccessAsync(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		var startedAt = Time.GetTimestamp();

		var evaluateTask = policyEvaluator.EvaluateAsync(principal, operation, ct);

		return evaluateTask.IsCompletedSuccessfully
			? new(HasAccess(evaluateTask.Result, principal, startedAt, LogAccessDenied, LogAccessGranted))
			: EnforceCheck(evaluateTask, principal, startedAt, LogAccessDenied, LogAccessGranted);

		static string GetIdentity(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Name) ?? "(anonymous)";

		static bool HasAccess(EvaluationResult result, ClaimsPrincipal principal, long startedAt, bool logAccessDenied, bool logAccessGranted) {
			var accessGranted = result.Grant == Grant.Allow;

			switch (accessGranted) {
				case true when logAccessGranted:
					Logger.Information(
						"Successful authorization check for {Identity} in {Duration} with {EvaluationResult}",
						GetIdentity(principal), Time.GetElapsedTime(startedAt), result
					);
					break;
				case false when logAccessDenied:
					Logger.Warning(
						"Failed authorization check for {Identity} in {Duration} with {EvaluationResult}",
						GetIdentity(principal), Time.GetElapsedTime(startedAt), result
					);
					break;
			}

			return accessGranted;
		}

		static async ValueTask<bool> EnforceCheck(
			ValueTask<EvaluationResult> evaluate, ClaimsPrincipal principal,
			long startedAt, bool logAccessDenied, bool logAccessGranted
		) {
			try {
				return HasAccess(await evaluate, principal, startedAt, logAccessDenied, logAccessGranted);
			}
			catch (Exception ex) when (ex is not OperationCanceledException) {
				Logger.Error(ex, "Error performing permission check for {Identity}", GetIdentity(principal));
				return false;
			}
		}
	}
}
