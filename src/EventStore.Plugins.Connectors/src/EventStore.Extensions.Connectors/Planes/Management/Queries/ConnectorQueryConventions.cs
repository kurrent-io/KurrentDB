using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Schema;
using static EventStore.Streaming.Consumers.ConsumeFilter;

namespace EventStore.Connectors.Management.Queries;

[PublicAPI]
public partial class ConnectorQueryConventions {
    [PublicAPI]
    public static class Streams {
        public static readonly StreamId ConnectorsStateProjectionStream            = "$connectors-mngt/connectors-state-projection";
        public static readonly StreamId ConnectorsStateProjectionCheckpointsStream = "$connectors-mngt/connectors-state-projection/checkpoints";
    }

    [PublicAPI]
    public partial class Filters {
        public const string ConnectorsStateProjectionStreamFilterPattern = @"^\$connectors\/([^\/]+)(\/checkpoints)?$";

        [GeneratedRegex(ConnectorsStateProjectionStreamFilterPattern)]
        private static partial Regex ConnectorsStateProjectionStreamFilterRegEx();

        public static readonly ConsumeFilter ConnectorsStateProjectionStreamFilter = FromRegex(ConsumeFilterScope.Stream, ConnectorsStateProjectionStreamFilterRegEx());
    }

    public static async Task<RegisteredSchema> RegisterQueryMessages<T>(ISchemaRegistry registry, CancellationToken token = default) {
        var schemaInfo = new SchemaInfo(
            ConnectorsFeatureConventions.Messages.GetManagementMessageSubject(typeof(T).Name),
            SchemaDefinitionType.Json
        );

        return await registry.RegisterSchema<T>(schemaInfo, cancellationToken: token);
    }
}