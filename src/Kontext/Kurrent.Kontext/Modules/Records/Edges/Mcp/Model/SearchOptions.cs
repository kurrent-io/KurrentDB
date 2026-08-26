// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Records.Mcp.Model;

// The contract groups stream/category and schemaName/schemaId/schemaFormat as oneofs, which JSON
// schema cannot express. They are five optional members here and the mapper rejects a request that
// sets more than one of a group — protobuf would otherwise keep whichever was assigned last and
// answer a question nobody asked.
public sealed class SearchOptions {
    public int Limit { get; set; }

    public double MinScore { get; set; }

    public string? Stream { get; set; }

    public string? Category { get; set; }

    public string? SchemaName { get; set; }

    public string? SchemaId { get; set; }

    public string? SchemaFormat { get; set; }
}
