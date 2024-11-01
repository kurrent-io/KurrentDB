using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Streaming;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Streaming.Contracts.Consumers;
using static System.StringComparison;

namespace EventStore.Connectors.Management.Data;

/// <summary>
/// Projects the current state of all connectors in the system.
/// </summary>
public class ConnectorsStateProjection : SnapshotProjectionsModule<ConnectorsSnapshot> {
    public ConnectorsStateProjection(
        Func<SystemReaderBuilder> getReaderBuilder, Func<SystemProducerBuilder> getProducerBuilder,
        string snapshotStreamId
    ) : base(getReaderBuilder, getProducerBuilder, snapshotStreamId) {
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

        UpdateWhen<Checkpoint>((snapshot, checkpoint) => {
            // right now, because we have a single instance per connector
            // the connectorId is the same as the consumerId
            return snapshot.Apply(checkpoint.ConsumerId, conn => {
                conn.Position           = checkpoint.LogPosition;
                conn.PositionUpdateTime = checkpoint.Timestamp;
                conn.UpdateTime         = checkpoint.Timestamp;
            });
        });
    }
}

public static class ConnectorsSnapshotExtensions {
    public static ConnectorsSnapshot ApplyOrAdd(this ConnectorsSnapshot snapshot, string connectorId, Action<Contracts.Queries.Connector> update) =>
        snapshot.With(ss =>
            ss.Connectors
                .FirstOrDefault(conn => conn.ConnectorId == connectorId, new Contracts.Queries.Connector())
                .With(connector => ss.Connectors.Add(connector), connector => !ss.Connectors.Contains(connector))
                .With(update));

    public static ConnectorsSnapshot Apply(this ConnectorsSnapshot snapshot, string connectorId, Action<Contracts.Queries.Connector> update) =>
        snapshot.With(ss => ss.Connectors.First(conn => conn.ConnectorId == connectorId).With(update));

    // // if not byref
    // public static ConnectorsSnapshot ApplyProper(this ConnectorsSnapshot snapshot, string connectorId, Action<Connector> update) {
    //     var connector = snapshot.Connectors
    //         .First(conn => conn.ConnectorId == connectorId);
    //
    //     snapshot.Connectors.Remove(connector);
    //     update(connector);
    //     snapshot.Connectors.Add(connector);
    //
    //     return snapshot;
    // }
}