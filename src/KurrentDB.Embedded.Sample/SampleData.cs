// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Client;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// The names and payloads the samples share. Everything is suffixed with a per-run id so that running
/// the sample twice against the same data directory does not read back the previous run's events.
/// </summary>
internal sealed class SampleData {
	public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];

	public string OrdersStream => $"orders-{RunId}";
	public string InventoryStream => $"inventory-{RunId}";
	public string ShipmentsStream => $"shipments-{RunId}";
	public string QuotesStream => $"quotes-{RunId}";

	public string OrderPlaced => $"OrderPlaced-{RunId}";
	public string OrderShipped => $"OrderShipped-{RunId}";
	public string StockReserved => $"StockReserved-{RunId}";

	public EventData Event<T>(string eventType, T payload) =>
		new(Uuid.NewUuid(), eventType, JsonSerializer.SerializeToUtf8Bytes(payload));

	public string Json<T>(T payload) => JsonSerializer.Serialize(payload);
}
