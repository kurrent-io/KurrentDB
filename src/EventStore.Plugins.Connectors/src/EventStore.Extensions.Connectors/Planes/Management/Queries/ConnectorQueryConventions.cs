using EventStore.Streaming;
using EventStore.Streaming.Schema;

namespace EventStore.Connectors.Management.Queries;

[PublicAPI]
public partial class ConnectorQueryConventions {
    [PublicAPI]
    public static class Streams {
        public static readonly StreamId ConnectorsStateProjectionStream            = "$connectors-mngt/state-projection";
        public static readonly StreamId ConnectorsStateProjectionCheckpointsStream = "$connectors-mngt/state-projection/checkpoints";
    }

    public static async Task<RegisteredSchema> RegisterQueryMessages<T>(ISchemaRegistry registry, CancellationToken token = default) {
        var schemaInfo = new SchemaInfo(
            ConnectorsFeatureConventions.Messages.GetManagementMessageSubject(typeof(T).Name),
            SchemaDefinitionType.Json
        );

        return await registry.RegisterSchema<T>(schemaInfo, cancellationToken: token);
    }
}