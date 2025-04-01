using System.Runtime.CompilerServices;

namespace EventStore.Toolkit.Testing.Fixtures;

public partial class FastFixture {
    public string NewStreamId([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();

    public string GenerateShortId()  => Guid.NewGuid().ToString()[30..];
    public string NewConnectorId()   => $"connector-id-{GenerateShortId()}".ToLowerInvariant();
    public string NewConnectorName() => $"connector-name-{GenerateShortId()}".ToLowerInvariant();
}