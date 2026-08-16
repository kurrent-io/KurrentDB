// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Quack;

/// <summary>
/// Convenience entry point for creating a configured <see cref="DuckDBDataSource"/>.
/// </summary>
public static class DuckDBDataSourceExtensions {
	extension(DuckDBDataSource) {
		/// <summary>
		/// Creates a data source over the database named by the connection string, configured
		/// through the callback.
		/// </summary>
		/// <remarks>
		/// The connection string seeds the options, so the callback can still redirect the data
		/// source through <see cref="DuckDBDataSourceOptions.ConnectToFile"/> or
		/// <see cref="DuckDBDataSourceOptions.ConnectToMemory"/>.
		/// </remarks>
		/// <param name="connectionString">The connection string.</param>
		/// <param name="configure">The configuration callback.</param>
		/// <returns>The data source.</returns>
		public static DuckDBDataSource Create(string connectionString, Action<DuckDBDataSourceOptions>? configure = null) {
			var options = new DuckDBDataSourceOptions { ConnectionString = connectionString };
			configure?.Invoke(options);
			return new(options);
		}

		/// <summary>
		/// Creates a data source from options that already name the database.
		/// </summary>
		/// <param name="options">The options.</param>
		/// <returns>The data source.</returns>
		public static DuckDBDataSource Create(DuckDBDataSourceOptions options) => new(options);
    }
}
