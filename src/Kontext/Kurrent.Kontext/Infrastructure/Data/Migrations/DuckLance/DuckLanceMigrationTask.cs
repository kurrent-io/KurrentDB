using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

public abstract class DuckLanceMigrationTask(string? name = null) : Migration<IDuckLanceSchemaExecutor>(name) {
    public override async ValueTask ExecuteAsync(IDuckLanceSchemaExecutor ctx, CancellationToken ct = default) =>
        await ctx.ExecuteAsync(conn => Execute(conn, ct), ct);

    protected abstract ValueTask Execute(DuckDBAdvancedConnection connection, CancellationToken ct = default);
}
