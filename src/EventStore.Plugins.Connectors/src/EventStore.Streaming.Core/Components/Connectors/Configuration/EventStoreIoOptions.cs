// ReSharper disable CheckNamespace

namespace EventStore.IO.Connectors.Configuration;

public record EventStoreIoOptions {
	public string?                    ConnectionString { get; init; } = "";
	public EventStoreConnectorOptions Connector        { get; init; } = new();
}

public record EventStoreConnectorOptions {
	public SinkOptions[] Sinks { get; init; } = [];
}
