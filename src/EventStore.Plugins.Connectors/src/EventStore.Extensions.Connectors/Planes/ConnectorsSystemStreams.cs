namespace EventStore.Connectors;

public static class ConnectorsSystemStreams {
    public const string StreamPrefix = "$connectors/";

    public static string GetManagementStream(string connectorId)   => $"{StreamPrefix}/{connectorId}";
    public static string GetLeasesStream(string connectorId)       => $"{StreamPrefix}/{connectorId}/lease";
    public static string GetPositionsStream(string connectorId)    => $"{StreamPrefix}/{connectorId}/positions";
    public static string GetStateChangesStream(string connectorId) => $"{StreamPrefix}/{connectorId}/state-changes";

    // lifetime:      $connectors/3f9728
    // leases:        $connectors/3f9728/lease
    // positions:     $connectors/3f9728/positions
    // state changes: $connectors/3f9728/state-changes
    //
    // IMPORTANT:
    // these streams can all be configured as a reaction to a connector being created,
    // and they can be deleted when the connector is deleted too (Note: metadata streams are not deleted)
    //
    // leases: max count 1 (playing with max age would require a bit of coupling between the db and the lease manager)
    // positions: max count 3 maybe
    // state changes: max count 10 maybe
}