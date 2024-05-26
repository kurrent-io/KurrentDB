namespace EventStore.Streaming;

public static class Identifiers {
    public static string GenerateShortId(string? prefix = null, string? suffix = null) {
        var id = Guid.NewGuid().ToString("N")[26..];
        return prefix is not null ? $"{prefix}-{id}" : suffix is not null ? $"{id}-{suffix}" : id;
    }

    public static string GenerateLongId(string? prefix = null, string? suffix = null) {
        var id = Guid.NewGuid().ToString("N");
        return prefix is not null ? $"{prefix}-{id}" : suffix is not null ? $"{id}-{suffix}" : id;
    }
}