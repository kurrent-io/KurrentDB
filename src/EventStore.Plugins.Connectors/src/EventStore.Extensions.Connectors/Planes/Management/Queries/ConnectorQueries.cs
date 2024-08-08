using EventStore.Common.Utils;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Projectors;
using EventStore.Streaming;

namespace EventStore.Connectors.Management.Queries;

public class ConnectorQueries(
    Func<SystemReaderBuilder> getReaderBuilder,
    ConnectorsStateProjectorOptions projectorOptions
) {
    SystemReader Reader           { get; } = getReaderBuilder().ReaderId("connector-queries-rdx").Create();
    StreamId     SnapshotStreamId { get; } = projectorOptions.SnapshotStreamId;

    public async Task<ListConnectorsResult> ListConnectors(
        ListConnectorsQuery query,
        CancellationToken cancellationToken
    ) {
        query.Paging ??= new Paging { Page = 1, PageSize = 100 };

        // TODO JC: Better but still needs to be improved.
        ConnectorQueryValidators.Validate(query);

        var state = await LoadLatestSnapshot(cancellationToken);

        var connectors = state.Connectors
            .Where(ConnectorSatisfiesFilters)
            .Skip(query.Paging.Page - (1 * query.Paging.PageSize))
            .Take(query.Paging.PageSize)
            .Select(MapToQuery)
            .ToList();

        return new ListConnectorsResult {
            Items      = { connectors },
            TotalCount = connectors.Count,
            Paging     = query.Paging
        };

        bool ConnectorSatisfiesFilters(ConnectorsSnapshot.Types.Connector connector) =>
            (query.State.IsEmpty()        || query.State.Contains(connector.State))               &&
            (query.InstanceType.IsEmpty() || query.InstanceType.Contains(connector.InstanceType)) &&
            (query.ConnectorId.IsEmpty()  || query.ConnectorId.Contains(connector.ConnectorId));

        static ListConnectorsResult.Types.Connector MapToQuery(ConnectorsSnapshot.Types.Connector connector) {
            return new ListConnectorsResult.Types.Connector {
                ConnectorId       = connector.ConnectorId,
                Name              = connector.Name,
                State             = connector.State,
                StateTimestamp    = connector.StateTimestamp,
                Settings          = { connector.Settings },
                SettingsTimestamp = connector.SettingsTimestamp,
                Position          = connector.Position
            };
        }
    }

    async Task<ConnectorsSnapshot> LoadLatestSnapshot(CancellationToken cancellationToken) {
        var snapshotRecord = await Reader.ReadLastStreamRecord(SnapshotStreamId, cancellationToken);

        return snapshotRecord.Value as ConnectorsSnapshot ?? new();
    }
}