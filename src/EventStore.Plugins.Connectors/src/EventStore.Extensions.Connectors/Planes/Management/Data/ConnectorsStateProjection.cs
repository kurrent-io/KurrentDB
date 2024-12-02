using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Toolkit;
using static System.StringComparison;

namespace EventStore.Connectors.Management.Data;

/// <summary>
/// Projects the current state of all connectors in the system.
/// </summary>
public class ConnectorsStateProjection : SnapshotProjectionsModule<ConnectorsSnapshot> {
    public ConnectorsStateProjection(ISnapshotProjectionsStore store, string snapshotStreamId) : base(store, snapshotStreamId) {
        UpdateWhen<ConnectorCreated>((snapshot, evt) =>
            snapshot.ApplyOrAdd(evt.ConnectorId, conn => {
                conn.Settings.Clear();
                conn.Settings.Add(evt.Settings);

                conn.ConnectorId        = evt.ConnectorId;
                conn.InstanceTypeName   = evt.Settings.First(kvp => kvp.Key.Equals(nameof(SinkOptions.InstanceTypeName), OrdinalIgnoreCase)).Value;
                conn.Name               = evt.Name;
                conn.State              = ConnectorState.Stopped;
                conn.StateUpdateTime    = evt.Timestamp;
                conn.SettingsUpdateTime = evt.Timestamp;
                conn.CreateTime         = evt.Timestamp;
                conn.UpdateTime         = evt.Timestamp;
                conn.DeleteTime         = null;
            }));

        UpdateWhen<ConnectorReconfigured>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.Settings.Clear();
                conn.Settings.Add(evt.Settings);
                conn.SettingsUpdateTime = evt.Timestamp;
                conn.UpdateTime         = evt.Timestamp;
            }));

        UpdateWhen<ConnectorRenamed>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.Name       = evt.Name;
                conn.UpdateTime = evt.Timestamp;
            }));

        UpdateWhen<ConnectorActivating>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.State           = ConnectorState.Activating;
                conn.StateUpdateTime = evt.Timestamp;
                conn.UpdateTime      = evt.Timestamp;
            }));

        UpdateWhen<ConnectorDeactivating>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.State           = ConnectorState.Deactivating;
                conn.StateUpdateTime = evt.Timestamp;
                conn.UpdateTime      = evt.Timestamp;
            }));

        UpdateWhen<ConnectorRunning>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.State           = ConnectorState.Running;
                conn.StateUpdateTime = evt.Timestamp;
                conn.UpdateTime      = evt.Timestamp;
            }));

        UpdateWhen<ConnectorStopped>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.State           = ConnectorState.Stopped;
                conn.StateUpdateTime = evt.Timestamp;
                conn.UpdateTime      = evt.Timestamp;
            }));

        UpdateWhen<ConnectorFailed>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.State           = ConnectorState.Stopped;
                conn.StateUpdateTime = evt.Timestamp;
                conn.UpdateTime      = evt.Timestamp;
                conn.ErrorDetails    = evt.ErrorDetails;
            }));

        UpdateWhen<ConnectorDeleted>((snapshot, evt) =>
            snapshot.Apply(evt.ConnectorId, conn => {
                conn.DeleteTime = evt.Timestamp;
                conn.UpdateTime = evt.Timestamp;
            }));
    }
}

public static class ConnectorsSnapshotExtensions {
    public static ConnectorsSnapshot ApplyOrAdd(this ConnectorsSnapshot snapshot, string connectorId, Action<Connector> update) =>
        snapshot.With(ss => ss.Connectors
            .FirstOrDefault(conn => conn.ConnectorId == connectorId, new Connector())
            .With(connector => ss.Connectors.Add(connector), connector => !ss.Connectors.Contains(connector))
            .With(update));

    public static ConnectorsSnapshot Apply(this ConnectorsSnapshot snapshot, string connectorId, Action<Connector> update) =>
        snapshot.With(ss => ss.Connectors.First(conn => conn.ConnectorId == connectorId).With(update));
}