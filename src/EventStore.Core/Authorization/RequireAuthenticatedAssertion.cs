using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization {
	internal class RequireAuthenticatedAssertion : IAssertion {
		public Grant Grant { get; } = Grant.Unknown;

		public AssertionInformation Information { get; } =
			new AssertionInformation("match", "authenticated", Grant.Unknown);

		public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
			EvaluationContext context) {
			if (!cp.Claims.Any() ||
				cp.Claims.Any(x => string.Equals(x.Type, ClaimTypes.Anonymous, StringComparison.Ordinal)))
				context.Add(new AssertionMatch(policy, new AssertionInformation("match", "authenticated", Grant.Deny)));
			else
				context.Add(new AssertionMatch(policy,
					new AssertionInformation("match", "authenticated", Grant.Allow)));
			return new ValueTask<bool>(true);
		}
	}
}
