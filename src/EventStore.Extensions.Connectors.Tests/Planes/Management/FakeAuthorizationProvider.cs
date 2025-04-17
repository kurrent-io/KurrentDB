using System.Security.Claims;
using EventStore.Plugins.Authorization;

namespace EventStore.Extensions.Connectors.Tests.Management;

public class FakeAuthorizationProvider : AuthorizationProviderBase {
    public bool ShouldGrantAccess { get; set; } = true;

    public override ValueTask<bool> CheckAccessAsync(
        ClaimsPrincipal principal,
        Operation operation,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(ShouldGrantAccess);
}