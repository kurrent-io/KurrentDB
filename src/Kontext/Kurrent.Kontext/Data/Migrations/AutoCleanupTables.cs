using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

namespace Kurrent.Kontext.Data.Migrations;

public sealed class AutoCleanupTables : DuckLanceMigrationScript {
    const int    CleanupIntervalCommits = 1000;
    const string CleanupOlderThan       = "1h";
    const int    CleanupRetainVersions  = 3;

    protected override string Generate() {
        var script =
            $"""
            ALTER TABLE ldb.main.memories SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits}, 
                older_than      = '{CleanupOlderThan}', 
                retain_versions = {CleanupRetainVersions}
            );

            ALTER TABLE ldb.main.records SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits}, 
                older_than      = '{CleanupOlderThan}', 
                retain_versions = {CleanupRetainVersions}
            )
            """;

        return script;
    }
}