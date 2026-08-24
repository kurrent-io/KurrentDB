// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;

namespace Kurrent.Kontext.Entities;

public sealed partial class KontextEntityResolver {
    /// <summary>Exact tier: a name matching a known alias links to its entity at full confidence.</summary>
    async ValueTask ResolveExactAsync(ResolutionPass pass, CancellationToken ct) {
        var aliases = await LookupExactAsync([.. pass.Undecided.Select(entry => entry.Key)], ct).ConfigureAwait(false);

        foreach (var (key, entityId) in aliases)
            pass.Decide(key, new ResolvedEntity(entityId, 1.0, ResolutionMethod.Exact));
    }

    /// <summary>Exact alias lookup per entity type. Unmatched names are absent.</summary>
    public async ValueTask<IReadOnlyDictionary<EntityKey, string>> LookupExactAsync(
        IReadOnlyCollection<EntityKey> keys, CancellationToken ct = default
    ) {
        if (keys.Count == 0)
            return new Dictionary<EntityKey, string>();

        return await dts.ExecuteAsync<IReadOnlyDictionary<EntityKey, string>>(
            connection => {
                var resolved = new Dictionary<EntityKey, string>();

                foreach (var group in keys.GroupBy(key => key.EntityType)) {
                    using var command = connection.CreateCommand();

                    command.CommandText =
                        """
                        SELECT lower(alias) AS matched, entity_id
                        FROM ldb.main.entities
                        WHERE entity_type = $entity_type
                          AND array_contains(CAST($aliases AS VARCHAR[]), lower(alias))
                        """;

                    command.Parameters.Add(new DuckDBParameter("entity_type", group.Key));
                    command.Parameters.Add(new DuckDBParameter("aliases", group.Select(key => key.NormalizedText).ToList()));

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        resolved.TryAdd(new EntityKey(group.Key, reader.GetString(0)), reader.GetString(1));
                }

                return resolved;
            }, ct
        ).ConfigureAwait(false);
    }
}
