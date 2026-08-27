// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Record = Kurrent.Kontext.Records.Mcp.Model.Record;
using RecordHit = Kurrent.Kontext.Records.Mcp.Model.RecordHit;
using SchemaInfo = Kurrent.Kontext.Records.Mcp.Model.SchemaInfo;
using QueryResult = Kurrent.Kontext.Records.Mcp.Model.QueryResult;
using SearchOptions = Kurrent.Kontext.Records.Mcp.Model.SearchOptions;
using SearchResult = Kurrent.Kontext.Records.Mcp.Model.SearchResult;
using RecordsContracts = Kurrent.Kontext.Contracts.Records;

namespace Kurrent.Kontext.Records.Mcp;

/// <summary>
/// Maps between the MCP edge's HTTP-friendly model (<c>Edges.Mcp.Model</c>) and the gRPC canonical
/// contract messages (<c>Kurrent.Kontext.Contracts</c>). Both sides declare colliding type names
/// (<c>Record</c>, <c>SchemaInfo</c>, …), so they are aliased throughout.
/// </summary>
static class McpRecordMappers {
    public static RecordsContracts.SearchRequest ToContract(string query, SearchOptions options) {
        // Assigning both halves of a protobuf oneof keeps only the last, so an over-specified request
        // would silently be answered under a filter the caller did not choose. Refuse it instead.
        if (options is { Stream: not null, Category: not null })
            throw new ArgumentException("stream and category are alternatives — a category is a set of streams. Set one, not both.");

        if (SchemaFiltersSet(options) > 1)
            throw new ArgumentException(
                "schemaName, schemaId and schemaFormat are alternatives — an id pins a version of a name, and a name implies its format. Set one, not several.");

        var request = new RecordsContracts.SearchRequest {
            Query    = query,
            Limit    = options.Limit,
            MinScore = options.MinScore,
        };

        if (options.Stream is { } stream)
            request.Stream = stream;
        else if (options.Category is { } category)
            request.Category = category;

        if (options.SchemaName is { } schemaName)
            request.SchemaName = schemaName;
        else if (options.SchemaId is { } schemaId)
            request.SchemaId = schemaId;
        else if (options.SchemaFormat is { } schemaFormat)
            request.SchemaFormat = schemaFormat;

        return request;
    }

    static int SchemaFiltersSet(SearchOptions options) =>
        (options.SchemaName is not null ? 1 : 0)
      + (options.SchemaId is not null ? 1 : 0)
      + (options.SchemaFormat is not null ? 1 : 0);

    public static QueryResult ToModel(RecordsContracts.QueryResponse response) =>
        new() {
            Truncated = response.Truncated,
            Rows      = [.. response.Rows.Select(ToJsonElement)],
        };

    static JsonElement ToJsonElement(Struct row) {
        using var document = JsonDocument.Parse(JsonFormatter.Default.Format(row));

        // Cloned — the element outlives the document.
        return document.RootElement.Clone();
    }

    public static SearchResult ToModel(RecordsContracts.SearchResponse response) =>
        new() { Hits = [.. response.Hits.Select(ToModel)] };

    static RecordHit ToModel(RecordsContracts.SearchResponse.Types.RecordHit hit) =>
        new() {
            Score  = hit.Score,
            Record = ToModel(hit.Record),
        };

    static Record ToModel(RecordsContracts.Record? record) =>
        record is null
            ? new()
            : new() {
                RecordId    = record.RecordId,
                Stream      = record.Stream,
                Category    = record.Category,
                LogPosition = record.LogPosition,
                Data        = record.Data,
                CreatedAt   = record.CreatedAt?.ToDateTimeOffset() ?? default,
                Schema      = ToModel(record.Schema),
                Properties  = ToModel(record.Properties),
            };

    // Formatted and reparsed rather than converted arm by arm — protobuf's formatter already knows how
    // every Value shape renders.
    static IReadOnlyDictionary<string, JsonElement> ToModel(MapField<string, Value> properties) {
        if (properties.Count == 0)
            return EmptyProperties;

        var wrapper  = new Struct();
        wrapper.Fields.Add(properties);

        using var document = JsonDocument.Parse(JsonFormatter.Default.Format(wrapper));

        // Cloned — the elements outlive the document.
        return document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    static readonly IReadOnlyDictionary<string, JsonElement> EmptyProperties = new Dictionary<string, JsonElement>();

    static SchemaInfo ToModel(RecordsContracts.SchemaInfo? schema) =>
        schema is null
            ? new()
            : new() {
                Format = schema.Format,
                Name   = schema.Name,
                Id     = schema.Id,
            };
}
