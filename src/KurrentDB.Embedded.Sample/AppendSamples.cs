// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using Serilog;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// Appending to one stream, and to several streams in one atomic transaction.
/// </summary>
internal sealed class AppendSamples(KurrentDBClient client, SampleData data) {
	public async Task RunAsync(CancellationToken cancellationToken) {
		await AppendToOneStreamAsync(cancellationToken);
		await AppendToManyStreamsAtomicallyAsync(cancellationToken);
	}

	async Task AppendToOneStreamAsync(CancellationToken cancellationToken) {
		var appended = await client.AppendToStreamAsync(
			data.OrdersStream,
			StreamState.Any,
			[
				data.Event(data.OrderPlaced, new { orderId = "A", country = "Mauritius" }),
				data.Event(data.OrderPlaced, new { orderId = "B", country = "United Kingdom" })
			],
			cancellationToken: cancellationToken);

		Log.Information(
			"Appended to {streamName}, now at revision {revision}",
			data.OrdersStream, appended.NextExpectedStreamState);
	}

	/// <summary>
	/// One transaction spanning two streams: either both appends land or neither does, and each stream
	/// carries its own expected state. This is the v2 append session, which the client reaches through
	/// <c>MultiStreamAppendAsync</c>; a stream may appear only once per call.
	/// </summary>
	async Task AppendToManyStreamsAtomicallyAsync(CancellationToken cancellationToken) {
		var response = await client.MultiStreamAppendAsync(
			[
				new AppendStreamRequest(
					data.OrdersStream,
					StreamState.Any,
					[data.Event(data.OrderShipped, new { orderId = "A", carrier = "DHL" })]),
				new AppendStreamRequest(
					data.InventoryStream,
					StreamState.Any,
					[data.Event(data.StockReserved, new { sku = "WIDGET-1", quantity = 2 })])
			],
			cancellationToken);

		foreach (var stream in response.Responses!)
			Log.Information("  {streamName} is now at revision {revision}", stream.Stream, stream.StreamRevision);

		Log.Information("Committed both streams at log position {position}", response.Position);
	}
}
