// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Kontext.Records.Mcp.Model;

public sealed class Record {
    public string RecordId { get; set; } = "";

    public string Stream { get; set; } = "";

    public string Category { get; set; } = "";

    public long LogPosition { get; set; }

    public SchemaInfo Schema { get; set; } = new();

    // JsonElement, because the contract types a property as google.protobuf.Value — any JSON value,
    // not just a string. Carrying it as an element keeps whatever the record actually held.
    public IReadOnlyDictionary<string, JsonElement> Properties { get; set; } = new Dictionary<string, JsonElement>();

    public string Data { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SchemaInfo {
    public string Format { get; set; } = "";

    public string Name { get; set; } = "";

    public string Id { get; set; } = "";
}
