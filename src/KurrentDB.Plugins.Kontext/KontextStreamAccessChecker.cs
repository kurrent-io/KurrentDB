using EventStore.Plugins.Authorization;
using Kurrent.Kontext;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Plugins.Kontext;

/// <summary>
/// Checks stream read access using KurrentDB's authorization provider.
/// Resolves the current user from HttpContext per request.
/// </summary>
public class KontextStreamAccessChecker(
	IAuthorizationProvider authorizationProvider,
	IHttpContextAccessor httpContextAccessor) : IKontextStreamAccessChecker {

	static readonly Operation ReadOperation = new(Operations.Streams.Read);

	public async ValueTask<bool> CanReadAsync(string stream, CancellationToken ct = default) {
		var user = httpContextAccessor.HttpContext?.User;
		if (user == null)
			return false;

		var op = ReadOperation.WithParameter(Operations.Streams.Parameters.StreamId(stream));
		return await authorizationProvider.CheckAccessAsync(user, op, ct);
	}
}
