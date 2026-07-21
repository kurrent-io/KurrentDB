using DuckDB.NET.Data;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Integration tests for <see cref="DuckDBEngineInfo"/> against a real DuckDB engine — plain
/// DuckDB, no <c>lance</c> extension involved, so no platform gate. The snapshot is the
/// get-the-info-first primitive: everything else (validation, attach verification) builds on it.
/// </summary>
public class DuckDBEngineInfoTests {
    [Test]
    public async Task Snapshot_From_A_Connection_String_Reports_The_Engine() {
        var dir = CreateTempStorageDir();

        try {
            var info = DuckDBEngineInfo.From($"Data Source={Path.Combine(dir, "engine.db")};memory_limit=512MiB");

            await Assert.That(info.Version).StartsWith("v");

            // The main catalog is named after the file stem, and its row carries the real file path.
            await Assert.That(info.CurrentDatabase).IsEqualTo("engine");
            await Assert.That(info.CurrentSchema).IsEqualTo("main");
            await Assert.That(info.MainDatabase.Type).IsEqualTo("duckdb");
            await Assert.That(info.MainDatabase.ReadOnly).IsFalse();
            await Assert.That(info.MainDatabase.Path).IsNotNull();
            await Assert.That(Path.GetFileName(info.MainDatabase.Path!)).IsEqualTo("engine.db");
            await Assert.That(File.Exists(info.MainDatabase.Path!)).IsTrue();

            // Settings arrive as the engine's own renderings; the connection-string value must show up.
            await Assert.That(info.Settings.ContainsKey("access_mode")).IsTrue();
            await Assert.That(info.Settings["memory_limit"]).Contains("512");

            // The name lookup is case-insensitive (DuckDB mixes casings like TimeZone/access_mode).
            await Assert.That(info.Settings.ContainsKey("timezone")).IsTrue();
        } finally {
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Snapshot_From_An_Open_Connection_Reports_Extensions_And_Attached_Databases() {
        var dir = CreateTempStorageDir();

        try {
            using var connection = new DuckDBConnection($"Data Source={Path.Combine(dir, "engine.db")}");
            connection.Open();

            // Attach a second database so the snapshot has a non-main catalog to report.
            using (var command = connection.CreateCommand()) {
                command.CommandText = $"ATTACH '{Path.Combine(dir, "other.db").Replace("'", "''")}' AS other";
                command.ExecuteNonQuery();
            }

            var info = DuckDBEngineInfo.From(connection);

            await Assert.That(info.IsExtensionLoaded("core_functions")).IsTrue();
            await Assert.That(info.FindDatabase("other")).IsNotNull();
            await Assert.That(info.FindDatabase("other")!.Type).IsEqualTo("duckdb");
            await Assert.That(info.FindDatabase("no_such_catalog")).IsNull();

            // Internal system/temp catalogs are reported, flagged as such — the snapshot hides nothing.
            await Assert.That(info.Databases.Any(database => database.Internal)).IsTrue();
        } finally {
            TryDeleteDir(dir);
        }
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-engineinfo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDeleteDir(string dir) {
        try {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        } catch (IOException) {
            // Best-effort cleanup.
        } catch (UnauthorizedAccessException) {
            // Best-effort cleanup.
        }
    }
}
