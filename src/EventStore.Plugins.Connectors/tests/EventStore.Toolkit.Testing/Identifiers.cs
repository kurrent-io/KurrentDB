namespace EventStore.Toolkit.Testing;

public static class Identifiers {
	public static string GenerateShortId(string? prefix = null) {
		var id = Guid.NewGuid().ToString("N")[26..];
		return prefix is not null ? $"{prefix}-{id}" : id;
	}

	public static string GenerateLongId(string? prefix = null) {
		var id = Guid.NewGuid().ToString("N");
		return prefix is not null ? $"{prefix}-{id}" : id;
	}
}