// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Readers.Configuration;

namespace KurrentDB.Connect.Readers.Configuration;

[PublicAPI]
public record SystemReaderOptions : ReaderOptions {
	public IPublisher Publisher { get; init; }
}
