// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace Kurrent.Kontext.Records.Data;

/// <summary>
/// Source-generated metadata for the one thing the records write path serializes: a record's headers,
/// stored as a JSON object in the <c>properties</c> column. Reflection-based serialization has no
/// fallback in a trimmed or AOT host.
/// <para>Read-side parsing does not come through here — the contract types properties as
/// <c>google.protobuf.Value</c>, so <c>Struct.Parser</c> reads that column instead.</para>
/// </summary>
[JsonSerializable(typeof(IDictionary<string, string?>))]
public sealed partial class RecordsJsonContext : JsonSerializerContext;
