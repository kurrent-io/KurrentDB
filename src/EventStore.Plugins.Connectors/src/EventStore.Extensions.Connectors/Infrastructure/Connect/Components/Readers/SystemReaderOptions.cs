// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using Kurrent.Surge.Readers.Configuration;

namespace EventStore.Connect.Readers.Configuration;

[PublicAPI]
public record SystemReaderOptions : ReaderOptions {
	public IPublisher Publisher { get; init; }
}