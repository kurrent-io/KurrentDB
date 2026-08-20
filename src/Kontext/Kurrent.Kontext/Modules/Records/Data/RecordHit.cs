// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Records.Data;

// A struct because Quack's query surface requires TRow to be a non-nullable value type.
public readonly record struct StoredRecord(
    long           LogPosition,
    Guid           RecordId,
    string         Stream,
    string         Category,
    string         SchemaName,
    string?        SchemaId,
    string         SchemaFormat,
    string?        Data,
    DateTimeOffset CreatedAt
);

public readonly record struct RecordHit(StoredRecord Record, double Score);

public sealed class HybridOptions {
    public required float[] QueryEmbedding { get; set; }

    // Free text. It runs against content_fts (code tokenizer), never data_fts, which only
    // accepts predicate triples and panics the engine on anything else.
    public required string Query { get; set; }

    public int K { get; set; } = 10;

    // The measured optimum the shipped Focused chain uses; keyword-leaning beats vector-leaning.
    public double Alpha { get; set; } = 0.45;
}

public sealed class SearchOptions {
    public IReadOnlyCollection<JsonPredicate> Predicates { get; set; } = [];

    // k is the engine's own parameter: how many ranked rows it returns.
    public int K { get; set; } = 10;
}
