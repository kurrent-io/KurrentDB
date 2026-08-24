using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

public interface IDuckLanceSchemaExecutor {
    ValueTask<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default);

    ValueTask ExecuteAsync(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default);
}
