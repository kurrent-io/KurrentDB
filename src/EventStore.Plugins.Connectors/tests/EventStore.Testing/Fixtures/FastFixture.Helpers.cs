namespace EventStore.Testing.Fixtures;

public partial class FastFixture {
	public string GenerateShortId() => Guid.NewGuid().ToString()[30..];
}