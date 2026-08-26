// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Kontext.Records.Mcp.Model;

public sealed class QueryResult {
    // JsonElement, because a row's shape is whatever the SELECT produced.
    public IReadOnlyList<JsonElement> Rows { get; set; } = [];

    public bool Truncated { get; set; }
}
