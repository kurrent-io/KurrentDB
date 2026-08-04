using System.Data.Common;
using DuckDB.NET.Data;
using DuckLance.Tests.Support;

namespace DuckLance.Tests;

/// <summary>
/// Smoke tests that verify the DuckDB <c>lance</c> extension can be installed, loaded, and
/// queried on this machine. Gated by <see cref="LanceRequiredAttribute"/> so it only runs on
/// platforms the extension actually ships binaries for.
/// </summary>
[LanceRequired]
[Category("Lance")]
public class LanceSmokeTests {
    [Test]
    public async ValueTask lance_extension_installs_loads_and_reports_version() {
        await using DuckDBConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();

        await using (DbCommand installCommand = connection.CreateCommand()) {
            installCommand.CommandText = "INSTALL lance; LOAD lance;";
            await installCommand.ExecuteNonQueryAsync();
        }

        var extensionVersion = await GetScalarStringAsync(
            connection,
            "SELECT extension_version FROM duckdb_extensions() WHERE extension_name = 'lance'");

        var duckDbVersion = await GetScalarStringAsync(connection, "SELECT version()");

        TestContext.Current?.OutputWriter.WriteLine($"lance extension_version: {extensionVersion}");
        TestContext.Current?.OutputWriter.WriteLine($"duckdb version(): {duckDbVersion}");

        await Assert.That(extensionVersion).IsNotNull();
        await Assert.That(extensionVersion!).IsNotEmpty();

        await Assert.That(duckDbVersion).IsNotNull();
        await Assert.That(duckDbVersion!).IsNotEmpty();
    }

    static async Task<string?> GetScalarStringAsync(DbConnection connection, string sql) {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result as string;
    }
}