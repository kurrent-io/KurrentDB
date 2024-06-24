namespace EventStore.Connectors;

/// <summary>
/// lifetime:      $connectors/3f9728
/// leases:        $connectors/3f9728/lease
/// positions:     $connectors/3f9728/positions
/// state changes: $connectors/3f9728/state
///
/// IMPORTANT:
/// these streams can all be configured as a reaction to a connector being created,
/// and they can be deleted when the connector is deleted too.
///
/// leases: max count 1 (playing with max age would require a bit of coupling between the db and the lease manager)
/// positions: max count 3 maybe
/// state changes: max count 10 maybe
/// </summary>
public static class ConnectorsSystemStreams {
    public const string ConnectorManagementStreamPrefix = "$connector-"; // example: $connectors/3f9728
}