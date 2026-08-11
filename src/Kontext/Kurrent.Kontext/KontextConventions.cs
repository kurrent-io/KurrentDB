// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;
using Humanizer;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Schema;

using static Kurrent.Surge.Consumers.ConsumeFilter;

namespace Kurrent.Kontext;

/// <summary>
/// Kontext's stream-space conventions in one place — the streams the write path appends to and
/// the consume filters the projector subscribes with — mirroring <c>SchemaRegistryConventions</c>.
/// </summary>
public partial class KontextConventions {
    public static class Streams {
        public const string KontextStreamPrefix  = "$kontext"; //$ktx/memories
        public const string MemoriesStreamPrefix = $"{KontextStreamPrefix}/memories";
    }

    // Copied from SchemaRegistryConventions.RegisterMessages (minus its Eventuous type mapping,
    // which Kontext does not use) until the primitive moves into Core — deduplicate then.
    public static async Task<RegisteredSchema> RegisterMessages<T>(ISchemaRegistry client, CancellationToken ct = default) {
        var schemaName = $"{Streams.MemoriesStreamPrefix}-{typeof(T).Name.Kebaberize()}";

        return await client.RegisterSchema<T>(
            new SchemaInfo(schemaName, SchemaDataFormat.Json),
            cancellationToken: ct
        );
    }

    public partial class Filters {
        // No trailing slash on purpose: matches the bare $kontext/memories stream today and any
        // $kontext/memories/... partitioning later, so the filter survives that decision.
        [GeneratedRegex($@"^\{Streams.MemoriesStreamPrefix}")]
        private static partial Regex GetMemoriesStreamFilterRegEx();

        public static readonly ConsumeFilter MemoriesFilter = FromRegex(ConsumeFilterScope.Stream, GetMemoriesStreamFilterRegEx());

        // The whole-log records index consumes everything except system events — which also
        // excludes every $kontext stream, so the indexer never eats its own exhaust.
        public static readonly ConsumeFilter RecordsIndexFilter = ExcludeSystemEvents();
    }
}
