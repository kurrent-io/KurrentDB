// ReSharper disable InconsistentNaming

using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Connectors.Testing;
using Humanizer;

namespace EventStore.Connect.Connectors;

public class SystemConnectorsValidation : ConnectorsValidationBase {
    static readonly Dictionary<string, IConnectorValidator> Validators = new() {
        { typeof(HttpSink).FullName!, new HttpSinkValidator() },
        { nameof(HttpSink), new HttpSinkValidator() },
        { nameof(HttpSink).Kebaberize(), new HttpSinkValidator() },

        { typeof(KafkaSink).FullName!, new KafkaSinkValidator() },
        { nameof(KafkaSink), new KafkaSinkValidator() },
        { nameof(KafkaSink).Kebaberize(), new KafkaSinkValidator() },

        { typeof(LoggerSink).FullName!, new LoggerSinkValidator() },
        { nameof(LoggerSink), new LoggerSinkValidator() },
        { nameof(LoggerSink).Kebaberize(), new LoggerSinkValidator() }
    };

    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator)
        => Validators.TryGetValue(connectorTypeName, out validator!);
}