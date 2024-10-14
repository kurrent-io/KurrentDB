// ReSharper disable InconsistentNaming

using EventStore.Connectors.EventStoreDB;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Connectors.Mongo;
using EventStore.Connectors.RabbitMQ;
using EventStore.Connectors.Testing;
using Humanizer;

namespace EventStore.Connect.Connectors;

public class SystemConnectorsValidation : ConnectorsValidationBase {
    static readonly Dictionary<string, IConnectorValidator> Validators = new() {
        { typeof(HttpSink).FullName!, HttpSinkValidator.Instance },
        { nameof(HttpSink), HttpSinkValidator.Instance },
        { nameof(HttpSink).Kebaberize(), HttpSinkValidator.Instance },

        { typeof(KafkaSink).FullName!, KafkaSinkValidator.Instance },
        { nameof(KafkaSink), KafkaSinkValidator.Instance },
        { nameof(KafkaSink).Kebaberize(), KafkaSinkValidator.Instance },

        { typeof(LoggerSink).FullName!, new LoggerSinkValidator() },
        { nameof(LoggerSink), new LoggerSinkValidator() },
        { nameof(LoggerSink).Kebaberize(), new LoggerSinkValidator() },

        { typeof(RabbitMqSink).FullName!, RabbitMqSinkValidator.Instance },
        { nameof(RabbitMqSink), RabbitMqSinkValidator.Instance },
        { nameof(RabbitMqSink).Kebaberize(), RabbitMqSinkValidator.Instance },

        { typeof(EventStoreDBSink).FullName!, EventStoreDBSinkValidator.Instance },
        { nameof(EventStoreDBSink), EventStoreDBSinkValidator.Instance },
        { nameof(EventStoreDBSink).Kebaberize(), EventStoreDBSinkValidator.Instance },

        { typeof(MongoDbSink).FullName!, new MongoSinkValidator() },
        { nameof(MongoDbSink), new MongoSinkValidator() },
        { nameof(MongoDbSink).Kebaberize(), new MongoSinkValidator() },
    };

    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator) =>
        Validators.TryGetValue(connectorTypeName, out validator!);
}