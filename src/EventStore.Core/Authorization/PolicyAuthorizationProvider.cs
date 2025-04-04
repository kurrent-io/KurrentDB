using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authorization;
using Serilog;

namespace EventStore.Core.Authorization {
	public class PolicyAuthorizationProvider : IAuthorizationProvider {
		private static readonly Stopwatch sw = Stopwatch.StartNew();
		private readonly bool _logAuthorization;
		private readonly ILogger _logger;
		private readonly bool _logSuccesses;
		private readonly IPolicyEvaluator _policyEvaluator;

		public PolicyAuthorizationProvider(IPolicyEvaluator policyEvaluator, ILogger logger, bool logAuthorization,
			bool logSuccesses) {
			_policyEvaluator = policyEvaluator;
			_logger = logger;
			_logAuthorization = logAuthorization;
			_logSuccesses = logSuccesses;
		}

		public ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) {
			if (cp == null)
				cp = SystemAccounts.Anonymous;

			var startedAt = sw.Elapsed;
			var evaluationTask = _policyEvaluator.EvaluateAsync(cp, operation, ct);
			if (evaluationTask.IsCompletedSuccessfully)
				return new ValueTask<bool>(LogAndCheck(startedAt, cp, evaluationTask.Result));

			return CheckAccessAsync(startedAt, cp, evaluationTask);
		}

		private async ValueTask<bool> CheckAccessAsync(TimeSpan startedAt, ClaimsPrincipal cp,
			ValueTask<EvaluationResult> evaluationTask) {
			try {
				return LogAndCheck(startedAt, cp, await evaluationTask.ConfigureAwait(false));
			} catch (Exception ex) when (ex is not OperationCanceledException) {
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
						cp.FindFirst(ClaimTypes.Name)?.Value ?? "(anonymous)", sw.Elapsed.Subtract(startedAt), result);
				else if (result.Grant != Grant.Allow)
					_logger.Warning(
						"Failed authorization check for {identity} in {duration} with {evaluationResult}",
						cp.FindFirst(ClaimTypes.Name)?.Value ?? "(anonymous)", sw.Elapsed.Subtract(startedAt), result);
			}

			return result.Grant == Grant.Allow;
		}
	}
}
