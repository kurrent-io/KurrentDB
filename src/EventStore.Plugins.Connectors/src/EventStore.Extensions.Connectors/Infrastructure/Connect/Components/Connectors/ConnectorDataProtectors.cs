// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Connectors.Elasticsearch;
using Kurrent.Connectors.Http;
using Kurrent.Connectors.Kafka;
using Kurrent.Connectors.KurrentDB;
using Kurrent.Connectors.MongoDB;
using Kurrent.Connectors.RabbitMQ;
using Kurrent.Connectors.Serilog;
using Kurrent.Surge.DataProtection;

namespace EventStore.Connectors.Connect.Components.Connectors;

[PublicAPI]
public class SerilogSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<SerilogSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() => [];
}

[PublicAPI]
public class KafkaSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<KafkaSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() => [
        "Authentication:Password"
    ];
}

[PublicAPI]
public class ElasticsearchSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<ElasticsearchSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() =>  [
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
public class RabbitMqSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<RabbitMqSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() => [
        "Authentication:Password"
    ];
}

[PublicAPI]
public class MongoDbSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<MongoDbSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() => [
        "Certificate:Password",
        "Certificate:RawData",
        "ConnectionString"
    ];
}

[PublicAPI]
public class HttpSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<HttpSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() => [
        "Authentication:Basic:Password",
        "Authentication:Bearer:Token"
    ];
}

[PublicAPI]
public class KurrentDbSinkConnectorDataProtector(IDataProtector dataProtector) : ConnectorDataProtector<KurrentDbSinkOptions>(dataProtector) {
    protected override string[] ConfigureSensitiveKeys() => [
        "ConnectionString"
    ];
}