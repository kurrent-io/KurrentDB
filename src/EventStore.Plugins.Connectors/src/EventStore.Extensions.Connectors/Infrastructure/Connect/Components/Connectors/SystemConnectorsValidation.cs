// ReSharper disable InconsistentNaming

using EventStore.Connectors.EventStoreDB;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Connectors.RabbitMQ;
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
        { nameof(LoggerSink).Kebaberize(), new LoggerSinkValidator() },

        { typeof(RabbitMqSink).FullName!, new RabbitMqSinkValidator() },
        { nameof(RabbitMqSink), new RabbitMqSinkValidator() },
        { nameof(RabbitMqSink).Kebaberize(), new RabbitMqSinkValidator() },

        { typeof(EventStoreDBSink).FullName!, new EventStoreDBSinkValidator() },
        { nameof(EventStoreDBSink), new EventStoreDBSinkValidator() },
        { nameof(EventStoreDBSink).Kebaberize(), new EventStoreDBSinkValidator() },
    };

    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator)
        => Validators.TryGetValue(connectorTypeName, out validator!);
}