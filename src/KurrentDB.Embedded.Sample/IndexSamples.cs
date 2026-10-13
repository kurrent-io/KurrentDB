// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Client;
using Serilog;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// Reading the event type secondary index, which KurrentDB 26.1 and later maintain as
/// <c>$idx-et-&lt;event type&gt;</c>.
/// </summary>
/// <remarks>
/// An index is a server-side filter over <c>$all</c> rather than a stream of links, so it is read with a
/// stream prefix filter on <c>ReadAllAsync</c> and not with <c>ReadStreamAsync</c>. One index name per
/// read, and progress is tracked by log position because index entries carry no event number of their own.
/// </remarks>
internal sealed class IndexSamples(KurrentDBClient client, SampleData data) {
	static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);
	static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

	public async Task RunAsync(CancellationToken cancellationToken) {
		var index = $"$idx-et-{data.OrderPlaced}";
		Log.Information("Reading the event type index {index}:", index);

		// indexing runs behind the write, so the first read of a just-written event type can come back empty
		var deadline = DateTimeOffset.UtcNow + Budget;
		while (true) {
			var found = 0;

			var events = client.ReadAllAsync(
				Direction.Forwards, Position.Start, StreamFilter.Prefix(index), maxCount: 1000,
				cancellationToken: cancellationToken);

			await foreach (var resolved in events) {
				found++;
				Log.Information(
					"  {position} {eventType} {data}",
					resolved.Event.Position.CommitPosition,
					resolved.Event.EventType,
					Encoding.UTF8.GetString(resolved.Event.Data.Span));
			}

			if (found > 0)
				return;

			if (DateTimeOffset.UtcNow >= deadline) {
				Log.Warning("  The index was still empty after {budget}", Budget);
				return;
			}

			await Task.Delay(PollInterval, cancellationToken);
		}
	}
}
