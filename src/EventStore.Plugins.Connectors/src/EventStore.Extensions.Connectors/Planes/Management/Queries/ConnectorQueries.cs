using EventStore.Common.Utils;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Connect.Components.Connectors;
using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Queries;
using Kurrent.Surge;
using Kurrent.Surge.Protocol.Consumers;
using Kurrent.Toolkit;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connectors.Management.Queries;

public class ConnectorQueries {
    public ConnectorQueries(Func<SystemReaderBuilder> getReaderBuilder, IConnectorDataProtector dataProtector, StreamId snapshotStreamId) {
        Reader        = getReaderBuilder().ReaderId("ConnectorQueriesReader").Create();
        DataProtector = dataProtector;

        LoadSnapshot = async token => {
            var snapshotRecord = await Reader.ReadLastStreamRecord(snapshotStreamId, token);
            return snapshotRecord.Value as ConnectorsSnapshot ?? new();
        };
    }

    SystemReader            Reader        { get; }
    IConnectorDataProtector DataProtector { get; }

    Func<CancellationToken, Task<ConnectorsSnapshot>> LoadSnapshot { get; }

    public async Task<ListConnectorsResult> List(ListConnectors query, CancellationToken cancellationToken) {
        query.Paging ??= new Paging { Page = 1, PageSize = 100 };

        var snapshot = await LoadSnapshot(cancellationToken);

        var skip = query.Paging.Page - (1 * query.Paging.PageSize);

        var items = await snapshot.Connectors.ToAsyncEnumerable()
            .Where(Filter())
            .Skip(skip)
            .Take(query.Paging.PageSize)
            .SelectAwaitWithCancellation(Map(query, cancellationToken))
            .SelectAwaitWithCancellation(EnrichWithPosition())
            .ToListAsync(cancellationToken);

        return new ListConnectorsResult {
            Items     = { items },
            TotalSize = items.Count
        };

        Func<Connector, bool> Filter() => conn =>
            (query.State.IsEmpty()            || query.State.Contains(conn.State))                       &&
            (query.InstanceTypeName.IsEmpty() || query.InstanceTypeName.Contains(conn.InstanceTypeName)) &&
            (query.ConnectorId.IsEmpty()      || query.ConnectorId.Contains(conn.ConnectorId))           &&
            (query.ShowDeleted ? conn.DeleteTime is not null : conn.DeleteTime is null);

        Func<Connector, CancellationToken, ValueTask<Connector>> EnrichWithPosition() =>
            async (conn, token) => {
                var checkpointStreamId = ConnectorsFeatureConventions.Streams.CheckpointsStreamTemplate.GetStream(conn.ConnectorId);
                var checkpointRecord   = await Reader.ReadLastStreamRecord(checkpointStreamId, token);
                return checkpointRecord.Value is Checkpoint checkpoint ? conn.With(x => x.Position = checkpoint.LogPosition) : conn;
            };
    }

    Func<Connector, CancellationToken, ValueTask<Connector>> Map(ListConnectors query, CancellationToken ct) =>
        async (conn, _) => {
            if (!query.IncludeSettings) {
                return conn.With(x => {
                    x.Settings.Clear();
                    return x;
                });
            }

            var unprotected = await DataProtector.Unprotect(conn.Settings.ToConfiguration(), ct);

            return conn.With(x => {
                x.Settings.Clear();

                foreach (var setting in unprotected.AsEnumerable()) {
                    if (setting.Value is not null)
                        x.Settings.Add(setting.Key, setting.Value);
                }

                return x;
            });
        };

    public async Task<GetConnectorSettingsResult> GetSettings(GetConnectorSettings query, CancellationToken cancellationToken) {
        var snapshot = await LoadSnapshot(cancellationToken);

        var connector = snapshot.Connectors.FirstOrDefault(x => x.ConnectorId == query.ConnectorId);

        if (connector is null)
            throw new DomainExceptions.EntityNotFound("Connector", query.ConnectorId);

        var unprotectedConfig   = await DataProtector.Unprotect(connector.Settings.ToConfiguration(), cancellationToken);
        var unprotectedSettings = new Dictionary<string, string>();

        foreach (var setting in unprotectedConfig.AsEnumerable()) {
            if (setting.Value is not null)
                unprotectedSettings.Add(setting.Key, setting.Value);
        }

        return new GetConnectorSettingsResult {
            Settings           = { unprotectedSettings },
            SettingsUpdateTime = connector.SettingsUpdateTime
        };

    }
 }