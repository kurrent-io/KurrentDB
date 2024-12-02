using System.Collections.Frozen;
using EventStore.Connectors.Elasticsearch;
using EventStore.Connectors.EventStoreDB;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Connectors.MongoDB;
using EventStore.Connectors.RabbitMQ;
using EventStore.Connectors.Serilog;
using Humanizer;

using static EventStore.Connectors.Connect.Components.Connectors.ConnectorCatalogueItem;

namespace EventStore.Connectors.Connect.Components.Connectors;

public class ConnectorCatalogue {
    public const string EntitlementPrefix = "CONNECTORS";

    static readonly ConnectorCatalogue Instance = new();

    FrozenDictionary<Type, ConnectorCatalogueItem>   Items        { get; }
    FrozenDictionary<string, ConnectorCatalogueItem> ItemsByAlias { get; }

    ConnectorCatalogue() {
        Items = new Dictionary<Type, ConnectorCatalogueItem> {
            [typeof(HttpSink)]     = For<HttpSink, HttpSinkValidator>([$"{EntitlementPrefix}_HTTP_SINK"], false),
            [typeof(SerilogSink)]  = For<SerilogSink, SerilogSinkValidator>([$"{EntitlementPrefix}_SERILOG_SINK"], false),
            [typeof(KafkaSink)]    = For<KafkaSink, KafkaSinkValidator>([$"{EntitlementPrefix}_KAFKA_SINK"], true),
            [typeof(RabbitMqSink)] = For<RabbitMqSink, RabbitMqSinkValidator>([$"{EntitlementPrefix}_RABBITMQ_SINK"], true),
            [typeof(EventStoreDbSink)] = For<EventStoreDbSink, EventStoreDbSinkValidator>([$"{EntitlementPrefix}_ESDB_SINK", $"{EntitlementPrefix}_ESDB_SOURCE"], true),
            [typeof(ElasticsearchSink)] = For<ElasticsearchSink, ElasticsearchSinkValidator>([$"{EntitlementPrefix}_ELASTICSEARCH_SINK", $"{EntitlementPrefix}_ELASTICSEARCH_SOURCE"], true),
            [typeof(MongoDbSink)] = For<MongoDbSink, MongoDbSinkValidator>([$"{EntitlementPrefix}_MONGODB_SINK"], true),
        }.ToFrozenDictionary();

        ItemsByAlias = Items
            .SelectMany(x => x.Value.Aliases.Select(alias => new KeyValuePair<string, ConnectorCatalogueItem>(alias, x.Value)))
            .ToFrozenDictionary();
    }

    public static bool TryGetConnector(string alias, out ConnectorCatalogueItem item) =>
        Instance.ItemsByAlias.TryGetValue(alias, out item);

    public static bool TryGetConnector(Type connectorType, out ConnectorCatalogueItem item) =>
        Instance.Items.TryGetValue(connectorType, out item);

    public static bool TryGetConnector<T>(out ConnectorCatalogueItem item) =>
        TryGetConnector(typeof(T), out item);

    public static IEnumerable<ConnectorCatalogueItem> GetConnectors() => Instance.Items.Values;

    public static Type[] ConnectorTypes() => Instance.Items.Keys.ToArray();
}

[PublicAPI]
public readonly record struct ConnectorCatalogueItem() {
    public static readonly ConnectorCatalogueItem None = new();

    public Type ConnectorType          { get; init; } = Type.Missing.GetType();
    public Type ConnectorValidatorType { get; init; } = Type.Missing.GetType();

    public bool     RequiresLicense      { get; init; } = true;
    public string[] RequiredEntitlements { get; init; } = [];
    public string[] Aliases              { get; init; } = [];

    public static ConnectorCatalogueItem For<T, TValidator>(string[] requiredEntitlements, bool requiresLicense) {
        return new ConnectorCatalogueItem {
            ConnectorType          = typeof(T),
            ConnectorValidatorType = typeof(TValidator),
            RequiredEntitlements   = requiredEntitlements,
            RequiresLicense        = requiresLicense,
            Aliases                = [typeof(T).FullName!, typeof(T).Name, typeof(T).Name.Kebaberize()]
        };
    }
}