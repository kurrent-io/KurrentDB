// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using Serilog;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// Soft deleting a stream, and putting a <c>$maxAge</c> on one.
/// </summary>
internal sealed class StreamManagementSamples(KurrentDBClient client, SampleData data) {
	public async Task RunAsync(CancellationToken cancellationToken) {
		await SoftDeleteAsync(cancellationToken);
		await SetMaxAgeAsync(cancellationToken);
	}

	/// <summary>
	/// A soft delete hides what is there and lets the stream be written again. The events are still in the
	/// log until a scavenge removes them, and the stream comes back at a higher revision rather than at 0 —
	/// which is the difference from a tombstone, after which the name cannot be reused at all.
	/// </summary>
	async Task SoftDeleteAsync(CancellationToken cancellationToken) {
		await client.AppendToStreamAsync(
			data.ShipmentsStream,
			StreamState.Any,
			[data.Event(data.OrderShipped, new { orderId = "A" })],
			cancellationToken: cancellationToken);

		await client.DeleteAsync(data.ShipmentsStream, StreamState.Any, cancellationToken: cancellationToken);

		var afterDelete = client.ReadStreamAsync(
			Direction.Forwards, data.ShipmentsStream, StreamPosition.Start,
			cancellationToken: cancellationToken);

		Log.Information(
			"{streamName} reads as {readState} after the soft delete",
			data.ShipmentsStream, await afterDelete.ReadState);

		var reappended = await client.AppendToStreamAsync(
			data.ShipmentsStream,
			StreamState.Any,
			[data.Event(data.OrderShipped, new { orderId = "B" })],
			cancellationToken: cancellationToken);

		Log.Information(
			"Writing again puts {streamName} back, at revision {revision} rather than 0",
			data.ShipmentsStream, reappended.NextExpectedStreamState);
	}

	/// <summary>
	/// <c>$maxAge</c> is stream metadata. Reads stop returning events older than it straight away; a
	/// scavenge is what eventually reclaims the space.
	/// </summary>
	async Task SetMaxAgeAsync(CancellationToken cancellationToken) {
		await client.AppendToStreamAsync(
			data.QuotesStream,
			StreamState.Any,
			[data.Event("QuoteGiven", new { price = 42 })],
			cancellationToken: cancellationToken);

		await client.SetStreamMetadataAsync(
			data.QuotesStream,
			StreamState.Any,
			new StreamMetadata(maxAge: TimeSpan.FromMinutes(10)),
			cancellationToken: cancellationToken);

		var metadata = await client.GetStreamMetadataAsync(data.QuotesStream, cancellationToken: cancellationToken);

		Log.Information("$maxAge on {streamName} is {maxAge}", data.QuotesStream, metadata.Metadata.MaxAge);
	}
}
