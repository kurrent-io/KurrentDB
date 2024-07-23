// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Readers.Configuration;

namespace EventStore.Connect.Readers.Configuration;

[PublicAPI]
public record SystemReaderOptions : ReaderOptions<SystemReaderOptions> {
	public SystemReaderOptions() {
		ReaderId = Identifiers.GenerateShortId("rdr");
	}

	public IPublisher Publisher { get; init; }
}