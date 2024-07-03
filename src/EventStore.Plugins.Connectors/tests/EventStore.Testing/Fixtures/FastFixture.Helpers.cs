namespace EventStore.Testing.Fixtures;

public partial class FastFixture {
    public string NewConnectorId() => $"connector-id-{GenerateShortId()}".ToLowerInvariant();
    public string NewConnectorName() => $"connector-name-{GenerateShortId()}".ToLowerInvariant();
	public string GenerateShortId() => Guid.NewGuid().ToString()[30..];
}