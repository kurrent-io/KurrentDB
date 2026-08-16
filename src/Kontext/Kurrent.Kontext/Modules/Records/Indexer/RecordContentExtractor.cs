// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Surge;
using Kurrent.Surge.Schema;

namespace Kurrent.Kontext.Modules.Records;

/// <summary>
/// The single authority over what the records index stores as searchable text. Returning
/// <see langword="null"/> skips the record entirely — no row is written. The extracted text
/// is both the FTS content and, verbatim, the embedding input.
/// </summary>
public delegate string? RecordContentExtractor(SurgeRecord record);

public static class KontextRecordsContent {
    /// <summary>
    /// The default extractor: a JSON payload is indexed as its complete text; anything the
    /// indexer cannot decode is skipped.
    /// </summary>
    public static string? Json(SurgeRecord record) =>
        record.SchemaInfo.SchemaDataFormat == SchemaDataFormat.Json
            ? Encoding.UTF8.GetString(record.Data.Span)
            : null;
}
