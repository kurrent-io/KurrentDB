// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Modules.Entities.Data;

/// <summary>
/// The resolution step's catalog reads, the seam where candidate generation grows (FTS, vector
/// similarity, ranking, LLM disambiguation).
/// </summary>
public sealed class KontextEntityResolver(KontextDataSource dataSource) {
    /// <summary>
    /// Maps each normalized surface form that exactly matches a stored alias to its entity id.
    /// An alias shared by several entities resolves to the first scanned, disambiguation is a
    /// later slice.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveExactAsync(
        IReadOnlyCollection<string> normalizedTexts, CancellationToken ct = default
    ) {
        if (normalizedTexts.Count == 0)
            return new Dictionary<string, string>();

        const string sql =
            """
            SELECT lower(alias) AS matched, entity_id
            FROM ldb.main.entities
            WHERE array_contains(CAST($aliases AS VARCHAR[]), lower(alias))
            """;

        return await dataSource.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.Add(new DuckDBParameter("aliases", normalizedTexts.ToList()));

                var resolved = new Dictionary<string, string>();

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    resolved.TryAdd(reader.GetString(0), reader.GetString(1));

                return (IReadOnlyDictionary<string, string>)resolved;
            }, ct
        ).ConfigureAwait(false);
    }
}
