using System.Text.Json;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// A point-in-time snapshot of everything a DuckDB engine reports about itself: version, attached
/// databases (with their file paths, types, and access), loaded extensions, and every setting.
/// This is the introspection primitive the connector builds on — get the info first, then create
/// or validate what needs it — and it is just as useful to hosts that bring their own engine.
/// </summary>
/// <remarks>
/// Column shapes validated live against DuckDB v1.5.3 (see the project knowledge base). Values in
/// <see cref="Settings"/> are the engine's own VARCHAR renderings (e.g. <c>memory_limit</c> comes
/// back as <c>"512.0 MiB"</c>), so compare semantically, not textually.
/// </remarks>
public sealed record DuckDBEngineInfo {
    public required string Version         { get; init; }
    public required string CurrentDatabase { get; init; }
    public required string CurrentSchema   { get; init; }

    /// <summary>Every attached catalog, including the engine's internal system/temp entries.</summary>
    public required IReadOnlyList<DuckDBDatabaseInfo> Databases { get; init; }

    public required IReadOnlyList<DuckDBExtensionInfo> Extensions { get; init; }

    /// <summary>Every engine setting by name (case-insensitive — DuckDB mixes casings like <c>TimeZone</c> and <c>access_mode</c>).</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>The current (default) database's entry — the engine file itself.</summary>
    public DuckDBDatabaseInfo MainDatabase => Databases.First(database => database.Name == CurrentDatabase);

    /// <summary>The attached database with the given name, or null — e.g. to verify an ATTACH landed with the expected type.</summary>
    public DuckDBDatabaseInfo? FindDatabase(string name) => Databases.FirstOrDefault(database => database.Name == name);

    public bool IsExtensionLoaded(string name) => Extensions.Any(extension => extension.Loaded && extension.Name == name);

    /// <summary>Reads the snapshot from an open connection — Kurrent.Quack's pooled connections qualify (they extend <see cref="DuckDBConnection"/>).</summary>
    public static DuckDBEngineInfo From(DuckDBConnection connection) {
        // The engine assembles the WHOLE snapshot as one JSON value (json extension — statically
        // linked in official builds): one statement, one scalar, named fields end to end, so there
        // is no positional read to misalign when the catalog functions grow columns. coalesce()
        // keeps the nullable text columns non-null, honoring the records' non-nullable contracts.
        using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT json_object(
              'version',          version(),
              'current_database', current_database(),
              'current_schema',   current_schema(),
              'databases',  (SELECT json_group_array(json_object(
                               'name', database_name, 'path', path, 'type', type,
                               'read_only', readonly, 'internal', internal)) FROM duckdb_databases()),
              'extensions', (SELECT json_group_array(json_object(
                               'name', extension_name, 'loaded', loaded, 'installed', installed,
                               'version', coalesce(extension_version, ''),
                               'install_mode', coalesce(install_mode, ''))) FROM duckdb_extensions()),
              'settings',   (SELECT json_group_object(name, coalesce(value, '')) FROM duckdb_settings())
            )
            """;

        var json     = (string)command.ExecuteScalar()!;
        var snapshot = JsonSerializer.Deserialize(json, DuckDBEngineInfoJsonContext.Default.DuckDBEngineInfo)!;

        // The deserializer builds a default-comparer dictionary; rebuild it case-insensitive
        // (DuckDB mixes setting-name casings like TimeZone and access_mode, while SET itself is
        // case-insensitive — consumers shouldn't have to know which casing the engine uses).
        return snapshot with { Settings = new Dictionary<string, string>(snapshot.Settings, StringComparer.OrdinalIgnoreCase) };
    }

    /// <summary>
    /// Opens a short-lived connection for the snapshot. Opening is not free of effects: DuckDB
    /// creates the database file if it is missing (and fails if its directory is), and the
    /// connection joins the shared in-process engine instance for that path.
    /// </summary>
    public static DuckDBEngineInfo From(string connectionString) {
        using var connection = new DuckDBConnection(connectionString);
        connection.Open();
        return From(connection);
    }
}

/// <summary>One attached catalog: the engine file, an ATTACHed database (e.g. a Lance namespace), or an internal system entry.</summary>
public sealed record DuckDBDatabaseInfo(string Name, string? Path, string Type, bool ReadOnly, bool Internal);

/// <summary>One extension the engine knows about, loaded or not.</summary>
public sealed record DuckDBExtensionInfo(string Name, bool Loaded, bool Installed, string Version, string InstallMode);

// Source-generated on purpose (the AOT rule: no reflection serializers); SnakeCaseLower matches the
// field names the introspection SQL emits.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DuckDBEngineInfo))]
sealed partial class DuckDBEngineInfoJsonContext : JsonSerializerContext;
