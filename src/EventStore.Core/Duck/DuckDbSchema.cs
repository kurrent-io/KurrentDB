using System.IO;
using System.Linq;
using System.Reflection;
using DuckDB.NET.Data;

namespace EventStore.Core.Duck;

public static class DuckDbSchema {
	static readonly Assembly Assembly = typeof(DuckDbSchema).Assembly;

	public static void CreateSchema(DuckDBConnection connection) {
		var names = Assembly.GetManifestResourceNames().Where(x => x.EndsWith(".sql")).OrderBy(x => x);
		using var transaction = connection.BeginTransaction();
		var cmd = connection.CreateCommand();
		cmd.Transaction = transaction;

		try {
			foreach (var name in names) {
				using var stream = Assembly.GetManifestResourceStream(name);
				using var reader = new StreamReader(stream!);
				var script = reader.ReadToEnd();

				cmd.CommandText = script;
				cmd.ExecuteNonQuery();
			}
		} catch {
			transaction.Rollback();
			throw;
		} finally {
			cmd.Dispose();
		}

		transaction.Commit();
	}
}
