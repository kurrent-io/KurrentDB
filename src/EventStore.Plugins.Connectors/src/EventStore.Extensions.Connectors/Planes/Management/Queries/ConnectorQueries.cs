using EventStore.Common.Utils;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Streaming;
using EventStore.Streaming.Contracts.Consumers;
using EventStore.Toolkit;

namespace EventStore.Connectors.Management.Queries;

public class ConnectorQueries {
    public ConnectorQueries(Func<SystemReaderBuilder> getReaderBuilder, StreamId snapshotStreamId) {
        Reader = getReaderBuilder().ReaderId("ConnectorQueriesReader").Create();

        LoadSnapshot = async token => {
            var snapshotRecord = await Reader.ReadLastStreamRecord(snapshotStreamId, token);
            return snapshotRecord.Value as ConnectorsSnapshot ?? new();
        };
    }

    SystemReader Reader { get;  }

    Func<CancellationToken, Task<ConnectorsSnapshot>> LoadSnapshot { get; }

    public async Task<ListConnectorsResult> List(ListConnectors query, CancellationToken cancellationToken) {
        query.Paging ??= new Paging { Page = 1, PageSize = 100 };

        var snapshot = await LoadSnapshot(cancellationToken);

        var skip = query.Paging.Page - (1 * query.Paging.PageSize);

        var items = await snapshot.Connectors.ToAsyncEnumerable()
            .Where(Filter())
            .Skip(skip)
            .Take(query.Paging.PageSize)
            .Select(Map())
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

        Func<Connector, Connector> Map() =>
            conn => query.IncludeSettings ? conn : conn.With(x => x.Settings.Clear());

        Func<Connector, CancellationToken, ValueTask<Connector>> EnrichWithPosition() =>
            async (conn, token) => {
                var checkpointStreamId = ConnectorsFeatureConventions.Streams.CheckpointsStreamTemplate.GetStream(conn.ConnectorId);
                var checkpointRecord   = await Reader.ReadLastStreamRecord(checkpointStreamId, token);
                return checkpointRecord.Value is Checkpoint checkpoint ? conn.With(x => x.Position = checkpoint.LogPosition) : conn;
            };
    }

    public async Task<GetConnectorSettingsResult> GetSettings(GetConnectorSettings query, CancellationToken cancellationToken) {
        var snapshot = await LoadSnapshot(cancellationToken);

        var connector = snapshot.Connectors.FirstOrDefault(x => x.ConnectorId == query.ConnectorId);

        if (connector is not null)
            return new GetConnectorSettingsResult {
                Settings           = { connector.Settings },
                SettingsUpdateTime = connector.SettingsUpdateTime
            };

        throw new DomainExceptions.EntityNotFound("Connector", query.ConnectorId);
    }
}