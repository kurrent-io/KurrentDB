using Grpc.Core;
using KurrentDB.Api.Streams.Authorization;

namespace KurrentDB.Api.Infrastructure.Authorization;

public static class PermissionsCatalog {
    static readonly Dictionary<string, Permission> Permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase) {
        { "/kurrentdb.api.v2.Streams/Append", StreamPermission.Append }
    };

    public static Permission? GetPermission(string method) =>
        Permissions.TryGetValue(method, out var permission) && permission != Permission.None
            ? permission : null;

    public static Permission? GetPermission(ServerCallContext context) =>
        GetPermission(context.Method);

    public static Permission GetRequiredPermission(string method) =>
        Permissions.TryGetValue(method, out var permission) && permission != Permission.None
            ? permission : throw new InvalidOperationException($"No permission defined for method '{method}'.");

    public static Permission GetRequiredPermission(ServerCallContext context) =>
        GetRequiredPermission(context.Method);
}
