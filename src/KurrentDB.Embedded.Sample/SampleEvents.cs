// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace KurrentDB.Embedded.Sample;

internal sealed record OrderPlaced(string OrderId, string Country);

internal sealed record OrderShipped(string OrderId, string? Carrier = null);

internal sealed record StockReserved(string Sku, int Quantity);

internal sealed record QuoteGiven(int Price);

/// <summary>
/// Source-generated serialization for the sample payloads, so the sample stays trim-safe.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderShipped))]
[JsonSerializable(typeof(StockReserved))]
[JsonSerializable(typeof(QuoteGiven))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;
