// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Records.Mcp.Model;

public sealed class SearchResult {
    public IReadOnlyList<RecordHit> Hits { get; set; } = [];
}

public sealed class RecordHit {
    public double Score { get; set; }

    public Record Record { get; set; } = new();
}
