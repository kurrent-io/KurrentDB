// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;

namespace Kurrent.Kontext.Modules.Records;

/// <summary>
/// The single authority over what the records index stores as searchable text. Returning
/// <see langword="null"/> skips the record entirely — no row is written. The extracted text
/// is both the FTS content and, verbatim, the embedding input.
/// </summary>
/// <param name="record">The resolved event under consideration.</param>
/// <param name="schemaFormat">
/// The record's data format, already resolved by the writer from the record's properties
/// (falling back to the IsJson flag) so the extractor never re-parses them.
/// </param>
public delegate string? ContentExtractor(in ResolvedEvent record, string schemaFormat);

public static class KontextRecordsContent {
    /// <summary>
    /// The default extractor: a JSON payload is indexed as its complete text; anything the
    /// indexer cannot decode is skipped.
    /// </summary>
    public static string? Json(in ResolvedEvent record, string schemaFormat) =>
        schemaFormat == "Json"
            ? Encoding.UTF8.GetString(record.Event.Data.Span)
            : null;
}
