// ReSharper disable CheckNamespace

using EventStore.Core.Bus;

namespace EventStore.Streaming.Readers.Configuration;

[PublicAPI]
public record SystemReaderOptions : ReaderOptions<SystemReaderOptions> {
	public SystemReaderOptions() {
		ReaderName = Identifiers.GenerateShortId("rdr");
	}

	public IPublisher Publisher { get; init; }
}