using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

public abstract class DuckLanceMigrationScript(string? name = null) : DuckLanceMigrationTask(name) {
    protected override ValueTask Execute(DuckDBAdvancedConnection connection, CancellationToken ct = default) {
        string script;
        try {
            script = Generate();
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to generate migration script for '{Name}': {ex.Message}", ex);
        }

        connection.ExecuteAdHocNonQuery(script, true);

        return ValueTask.CompletedTask;
    }

    protected abstract string Generate();
}
