// ReSharper disable CheckNamespace

using System.Diagnostics.CodeAnalysis;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Streaming;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connect.Connectors;

public class SystemConnectorsValidation : ConnectorsValidationBase {
    Dictionary<string, IConnectorValidator> Validators { get; } = new() {
        { typeof(HttpSink).FullName!, new HttpSinkValidator() },
        { typeof(KafkaSink).FullName!, new KafkaSinkValidator() },
        { typeof(LoggerSink).FullName!, new LoggerSinkValidator() }
    };

    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator) =>
        Validators.TryGetValue(connectorTypeName, out validator!);
}