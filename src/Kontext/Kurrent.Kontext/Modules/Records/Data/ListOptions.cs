// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Records.Data;

public sealed class ListOptions {
    public Guid[]? RecordIds { get; set; }

    public long[]? LogPositions { get; set; }

    public string? Stream { get; set; }

    public string? Category { get; set; }

    public string? SchemaName { get; set; }

    public string? SchemaFormat { get; set; }

    // Cursor, not an offset: the log only grows, so it stays valid across pages.
    public long AfterLogPosition { get; set; }

    public int Limit { get; set; } = 100;
}
