// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Connectors.Elasticsearch;
using Kurrent.Connectors.Http;
using Kurrent.Connectors.Kafka;
using Kurrent.Connectors.KurrentDB;
using Kurrent.Connectors.MongoDB;
using Kurrent.Connectors.RabbitMQ;
using Kurrent.Connectors.Serilog;

namespace EventStore.Connectors.Connect.Components.Connectors;

[PublicAPI]
public class SerilogSinkConnectorDataProtector : ConnectorDataProtector<SerilogSinkOptions>;

[PublicAPI]
public class KafkaSinkConnectorDataProtector : ConnectorDataProtector<KafkaSinkOptions> {
    public override string[] Keys => [
        "Authentication:Password"
    ];
}

[PublicAPI]
public class ElasticsearchSinkConnectorDataProtector : ConnectorDataProtector<ElasticsearchSinkOptions> {
    public override string[] Keys => [
        "Authentication:Password",
        "Authentication:ClientCertificate:Password",
        "Authentication:RootCertificate:Password",
        "Authentication:RootCertificate:RawData",
        "Authentication:ClientCertificate:RawData",
        "Authentication:ApiKey",
        "Authentication:Base64ApiKey"
    ];
}

[PublicAPI]
public class RabbitMqSinkConnectorDataProtector : ConnectorDataProtector<RabbitMqSinkOptions> {
    public override string[] Keys => [
        "Authentication:Password"
    ];
}

[PublicAPI]
public class MongoDbSinkConnectorDataProtector : ConnectorDataProtector<MongoDbSinkOptions> {
    public override string[] Keys => [
        "Certificate:Password",
        "Certificate:RawData",
        "ConnectionString"
    ];
}

[PublicAPI]
public class HttpSinkConnectorDataProtector : ConnectorDataProtector<HttpSinkOptions> {
    public override string[] Keys => [
        "Authentication:Basic:Password",
        "Authentication:Bearer:Token"
    ];
}

[PublicAPI]
public class KurrentDbSinkConnectorDataProtector : ConnectorDataProtector<KurrentDbSinkOptions> {
    public override string[] Keys => [
        "ConnectionString"
    ];
}