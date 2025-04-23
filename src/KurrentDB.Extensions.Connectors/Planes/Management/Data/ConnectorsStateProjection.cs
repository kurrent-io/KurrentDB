using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Connectors.Management.Contracts;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Management.Contracts.Queries;
using Kurrent.Surge;
using Kurrent.Surge.Connectors.Sinks;
using Kurrent.Toolkit;
using KurrentDB.Core.Services.Transport.Enumerators;
using static System.StringComparison;

namespace KurrentDB.Connectors.Planes.Management.Data;

public interface IConnectorsStateProjection {
    Task<(LogPosition Position, DateTimeOffset Timestamp)> WaitUntilCaughtUp { get; }

    bool IsCaughtUp => WaitUntilCaughtUp.IsCompleted;
}

/// <summary>
/// Projects the current state of all connectors in the system.
/// </summary>
public class ConnectorsStateProjection : SnapshotProjectionsModule<ConnectorsSnapshot>, IConnectorsStateProjection {
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

        Process<ReadResponse.SubscriptionCaughtUp>((_, ctx) => {
            if (!HasCaughtUpTaskCompletionSource.Task.IsCompleted)
                HasCaughtUpTaskCompletionSource.SetResult((ctx.Record.Position, ctx.Record.Timestamp));
        });
    }

    TaskCompletionSource<(LogPosition Position, DateTimeOffset Timestamp)> HasCaughtUpTaskCompletionSource { get; } = new();

    public Task<(LogPosition Position, DateTimeOffset Timestamp)> WaitUntilCaughtUp => HasCaughtUpTaskCompletionSource.Task;
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
