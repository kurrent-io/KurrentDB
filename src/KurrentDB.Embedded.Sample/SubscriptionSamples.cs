// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using Serilog;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// Catch-up subscriptions to <c>$all</c> and to a single stream. The <c>$all</c> filters are evaluated on
/// the server, so an unmatched event never reaches the client at all.
/// </summary>
/// <remarks>
/// Each of these subscribes from the start of the log, takes the events this run wrote, and stops. A real
/// subscription stays open and keeps its own checkpoint; the budget here is only so the sample finishes.
/// </remarks>
internal sealed class SubscriptionSamples(KurrentDBClient client, SampleData data) {
	static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

	public async Task RunAsync(CancellationToken cancellationToken) {
		await SubscribeToAllByEventTypeRegexAsync(cancellationToken);
		await SubscribeToAllByStreamPrefixAsync(cancellationToken);
		await SubscribeToOneStreamAsync(cancellationToken);
	}

	/// <summary>A regular expression over the event type, matched by the server.</summary>
	async Task SubscribeToAllByEventTypeRegexAsync(CancellationToken cancellationToken) {
		var pattern = $"^Order.*-{data.RunId}$";
		Log.Information("$all filtered by event type matching {pattern}:", pattern);

		await using var subscription = client.SubscribeToAll(
			FromAll.Start,
			filterOptions: new SubscriptionFilterOptions(EventTypeFilter.RegularExpression(pattern)),
			cancellationToken: cancellationToken);

		// two OrderPlaced from the single-stream append, one OrderShipped from the atomic one
		await TakeAsync(subscription, count: 3, cancellationToken);
	}

	/// <summary>A stream name prefix, matched by the server.</summary>
	async Task SubscribeToAllByStreamPrefixAsync(CancellationToken cancellationToken) {
		Log.Information("$all filtered by stream prefix {prefix}:", data.InventoryStream);

		await using var subscription = client.SubscribeToAll(
			FromAll.Start,
			filterOptions: new SubscriptionFilterOptions(StreamFilter.Prefix(data.InventoryStream)),
			cancellationToken: cancellationToken);

		await TakeAsync(subscription, count: 1, cancellationToken);
	}

	/// <summary>No filter needed: a single stream is already the subset.</summary>
	async Task SubscribeToOneStreamAsync(CancellationToken cancellationToken) {
		Log.Information("Subscription to {streamName}:", data.OrdersStream);

		await using var subscription = client.SubscribeToStream(
			data.OrdersStream,
			FromStream.Start,
			cancellationToken: cancellationToken);

		await TakeAsync(subscription, count: 3, cancellationToken);
	}

	static async Task TakeAsync(IAsyncEnumerable<ResolvedEvent> subscription, int count, CancellationToken cancellationToken) {
		using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		budget.CancelAfter(Budget);

		var seen = 0;
		try {
			await foreach (var resolved in subscription.WithCancellation(budget.Token)) {
				Log.Information("  {streamName}@{revision} {eventType}",
					resolved.Event.EventStreamId, resolved.Event.EventNumber, resolved.Event.EventType);

				if (++seen == count)
					return;
			}
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			Log.Warning("  Gave up after {seen} of {count} events", seen, count);
		}
	}
}
