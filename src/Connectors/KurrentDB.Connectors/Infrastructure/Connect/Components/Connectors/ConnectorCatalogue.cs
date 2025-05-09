// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;
using Humanizer;
using Kurrent.Connectors.Elasticsearch;
using Kurrent.Connectors.Http;
using Kurrent.Connectors.Kafka;
using Kurrent.Connectors.KurrentDB;
using Kurrent.Connectors.MongoDB;
using Kurrent.Connectors.RabbitMQ;
using Kurrent.Connectors.Serilog;
using static KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors.ConnectorCatalogueItem;

namespace KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;

public class ConnectorCatalogue {
    public const string EntitlementPrefix = "CONNECTORS";

    public static readonly ConnectorCatalogue Instance = new();

    FrozenDictionary<Type, ConnectorCatalogueItem>   Items        { get; }
    FrozenDictionary<string, ConnectorCatalogueItem> ItemsByAlias { get; }

    ConnectorCatalogue() {
        Items = new Dictionary<Type, ConnectorCatalogueItem> {
            [typeof(HttpSink)]          = For<HttpSink, HttpSinkValidator, HttpSinkConnectorDataProtector>([$"{EntitlementPrefix}_HTTP_SINK"], false),
            [typeof(SerilogSink)]       = For<SerilogSink, SerilogSinkValidator, SerilogSinkConnectorDataProtector>([$"{EntitlementPrefix}_SERILOG_SINK"], false),
            [typeof(KafkaSink)]         = For<KafkaSink, KafkaSinkValidator, KafkaSinkConnectorDataProtector>([$"{EntitlementPrefix}_KAFKA_SINK"], true),
            [typeof(RabbitMqSink)]      = For<RabbitMqSink, RabbitMqSinkValidator, RabbitMqSinkConnectorDataProtector>([$"{EntitlementPrefix}_RABBITMQ_SINK"], true),
            [typeof(KurrentDbSink)]     = For<KurrentDbSink, KurrentDbSinkValidator, KurrentDbSinkConnectorDataProtector>([$"{EntitlementPrefix}_KURRENTDB_SINK", $"{EntitlementPrefix}_KURRENTDB_SOURCE"], true),
            [typeof(ElasticsearchSink)] = For<ElasticsearchSink, ElasticsearchSinkValidator, ElasticsearchSinkConnectorDataProtector>([$"{EntitlementPrefix}_ELASTICSEARCH_SINK", $"{EntitlementPrefix}_ELASTICSEARCH_SOURCE"], true),
            [typeof(MongoDbSink)]       = For<MongoDbSink, MongoDbSinkValidator, MongoDbSinkConnectorDataProtector>([$"{EntitlementPrefix}_MONGODB_SINK"], true),
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
    public Type ConnectorProtectorType { get; init; } = Type.Missing.GetType();

    public bool     RequiresLicense      { get; init; } = true;
    public string[] RequiredEntitlements { get; init; } = [];
    public string[] Aliases              { get; init; } = [];

    public static ConnectorCatalogueItem For<T, TValidator, TProtector>(string[] requiredEntitlements, bool requiresLicense) {
        return new ConnectorCatalogueItem {
            ConnectorType          = typeof(T),
            ConnectorValidatorType = typeof(TValidator),
            ConnectorProtectorType = typeof(TProtector),
            RequiredEntitlements   = requiredEntitlements,
            RequiresLicense        = requiresLicense,
            Aliases                = [typeof(T).FullName!, typeof(T).Name, typeof(T).Name.Kebaberize()]
        };
    }
}