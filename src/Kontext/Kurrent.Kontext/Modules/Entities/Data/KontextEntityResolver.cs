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
    /// Maps each key whose normalized surface form exactly matches a stored alias of the same
    /// entity type to its entity id. An alias shared by several entities of one type resolves to
    /// the first scanned, disambiguation is a later slice.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<EntityKey, string>> ResolveExactAsync(
        IReadOnlyCollection<EntityKey> keys, CancellationToken ct = default
    ) {
        if (keys.Count == 0)
            return new Dictionary<EntityKey, string>();

        const string sql =
            """
            SELECT lower(alias) AS matched, entity_id
            FROM ldb.main.entities
            WHERE entity_type = $entity_type
              AND array_contains(CAST($aliases AS VARCHAR[]), lower(alias))
            """;

        return await dataSource.ExecuteAsync(
            connection => {
                var resolved = new Dictionary<EntityKey, string>();

                foreach (var group in keys.GroupBy(key => key.EntityType)) {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter("entity_type", group.Key));
                    command.Parameters.Add(new DuckDBParameter("aliases", group.Select(key => key.NormalizedText).ToList()));

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        resolved.TryAdd(new EntityKey(group.Key, reader.GetString(0)), reader.GetString(1));
                }

                return (IReadOnlyDictionary<EntityKey, string>)resolved;
            }, ct
        ).ConfigureAwait(false);
    }
}
