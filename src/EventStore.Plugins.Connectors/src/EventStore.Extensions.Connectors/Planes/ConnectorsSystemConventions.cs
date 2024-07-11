using Humanizer;

namespace EventStore.Connectors;

// lifetime:      $connectors/3f9728
// leases:        $connectors/3f9728/lease
// positions:     $connectors/3f9728/positions
// state changes: $connectors/3f9728/lifetime
//
// IMPORTANT:
// these streams can all be configured as a reaction to a connector being created,
// and they can be deleted when the connector is deleted too (Note: metadata streams are not deleted)
//
// leases: max count 1 (playing with max age would require a bit of coupling between the db and the lease manager)
// positions: max count 3 maybe
// state changes: max count 10 maybe

[PublicAPI]
public static class ConnectorsSystemConventions {
    public const string StreamPrefix = "$connectors";

    public static string GetManagementStream(string connectorId) => $"{StreamPrefix}/{connectorId}";
    public static string GetLeasesStream(string connectorId)     => $"{StreamPrefix}/{connectorId}/leases";
    public static string GetPositionsStream(string connectorId)  => $"{StreamPrefix}/{connectorId}/positions";
    public static string GetLifetimeStream(string connectorId)   => $"{StreamPrefix}/{connectorId}/lifetime";

    // public static string GetManagementStream(string connectorId) => $"{StreamPrefix}/{connectorId}";
    // public static string GetLeasesStream(string connectorId)     => $"{StreamPrefix}/leases/{connectorId}";
    // public static string GetPositionsStream(string connectorId)  => $"{StreamPrefix}/positions/{connectorId}";
    // public static string GetLifetimeStream(string connectorId)   => $"{StreamPrefix}/lifetime/{connectorId}";

    public static string GetSystemEventName(string prefix, string name) => $"${prefix}-{name.Kebaberize()}";

    public static string GetManagementSystemEventName(string name) => GetSystemEventName("mngt", name);
    public static string GetLeasesSystemEventName(string name)     => GetSystemEventName("ctrl", name);
    public static string GetPositionsSystemEventName(string name)  => GetSystemEventName("ctrl", name);
    public static string GetLifetimeSystemEventName(string name)   => GetSystemEventName("ctrl", name);

    public static string GetManagementSystemEventName<T>() => GetManagementSystemEventName(typeof(T).Name);
    public static string GetLeasesSystemEventName<T>()     => GetLeasesSystemEventName(typeof(T).Name);
    public static string GetPositionsSystemEventName<T>()  => GetPositionsSystemEventName(typeof(T).Name);
    public static string GetLifetimeSystemEventName<T>()   => GetLifetimeSystemEventName(typeof(T).Name);
}