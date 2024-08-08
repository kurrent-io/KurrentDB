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
    [SuppressMessage("Usage", "CA2211:Non-constant fields should not be visible")]
    public static class Streams {
        public const  string   StreamPrefix                    = "$connectors";
        public static StreamId ConnectorsQueryProjectionStream = $"{StreamPrefix}-projection";
    }

    [PublicAPI]
    public partial class Filters {
        public const string ConnectorsQueryStateFilterPattern = @"^\$connectors\/([^\/]+)(\/(lifecycle|checkpoints))?$";

        [GeneratedRegex(ConnectorsQueryStateFilterPattern)]
        private static partial Regex GetConnectorsQueryStateStreamFilterRegEx();

        public static readonly ConsumeFilter ConnectorsQueryStateFilter =
            FromRegex(ConsumeFilterScope.Stream, GetConnectorsQueryStateStreamFilterRegEx());
    }

    public static async Task<RegisteredSchema> RegisterQueryMessages<T>(
        ISchemaRegistry registry, CancellationToken token = default
    ) {
        var schemaInfo =
            new SchemaInfo(ConnectorsFeatureConventions.Messages.GetControlSystemMessageSubject(typeof(T).Name),
                SchemaDefinitionType.Json);

        return await registry.RegisterSchema<T>(schemaInfo, cancellationToken: token);
    }
}