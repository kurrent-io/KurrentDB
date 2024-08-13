using EventStore.Common.Utils;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Streaming;

namespace EventStore.Connectors.Management.Queries;

public class ConnectorQueries {
    public ConnectorQueries(Func<SystemReaderBuilder> getReaderBuilder, StreamId snapshotStreamId) {
        var reader = getReaderBuilder().ReaderId("connector-queries-rdx").Create();

        LoadSnapshot = async token => {
            var snapshotRecord = await reader.ReadLastStreamRecord(snapshotStreamId, token);
            return snapshotRecord.Value as ConnectorsSnapshot ?? new();
        };
    }

    Func<CancellationToken, Task<ConnectorsSnapshot>> LoadSnapshot { get; }

    public async Task<ListConnectorsResult> List(ListConnectors query, CancellationToken cancellationToken) {
        query.Paging ??= new Paging { Page = 1, PageSize = 100 };

        // TODO JC: Better but still needs to be improved.
        ListConnectorsQueryValidator.EnsureValid(query);

        var snapshot = await LoadSnapshot(cancellationToken);

        var skip = query.Paging.Page - (1 * query.Paging.PageSize);

        var items = snapshot.Connectors
            .Where(Filter())
            .Skip(skip)
            .Take(query.Paging.PageSize)
            .Select(Map())
            .ToList();

        return new ListConnectorsResult {
            Items     = { items },
            TotalSize = items.Count
        };

        Func<Connector, bool> Filter() => conn =>
            (query.State.IsEmpty()        || query.State.Contains(conn.State))                   &&
            (query.InstanceType.IsEmpty() || query.InstanceType.Contains(conn.InstanceTypeName)) &&
            (query.ConnectorId.IsEmpty()  || query.ConnectorId.Contains(conn.ConnectorId))       &&
            (query.ShowDeleted ? conn.DeleteTime is not null : conn.DeleteTime is null);

        Func<Connector, Connector> Map() =>
            conn => query.IncludeSettings ? conn : conn.With(x => x.Settings.Clear());
    }

    public async Task<GetConnectorSettingsResult> GetSettings(GetConnectorSettings query, CancellationToken cancellationToken) {
        GetConnectorSettingsQueryValidator.EnsureValid(query);

        var snapshot = await LoadSnapshot(cancellationToken);

        var connector = snapshot.Connectors.FirstOrDefault(x => x.ConnectorId == query.ConnectorId);

        if (connector is not null)
            return new GetConnectorSettingsResult {
                Settings           = { connector.Settings },
                SettingsUpdateTime = connector.SettingsUpdateTime
            };

        return new ();
        // TODO SS: how to throw here? dont want any grpc coupling should we return a result tuple like <query-result, error>?
        //throw new RpcExceptions.NotFound($"Connector with id {query.ConnectorId} not found.");
    }
}