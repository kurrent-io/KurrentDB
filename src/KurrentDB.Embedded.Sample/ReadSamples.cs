// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Client;
using Serilog;

namespace KurrentDB.Embedded.Sample;

/// <summary>
/// Reading a single stream and the global log, in both directions. Backwards reads start from the end:
/// <c>StreamPosition.End</c> for a stream, <c>Position.End</c> for <c>$all</c>.
/// </summary>
internal sealed class ReadSamples(KurrentDBClient client, SampleData data) {
	const long MaxCount = 100;

	public async Task RunAsync(CancellationToken cancellationToken) {
		await ReadStreamForwardsAsync(cancellationToken);
		await ReadStreamBackwardsAsync(cancellationToken);
		await ReadAllForwardsAsync(cancellationToken);
		await ReadAllBackwardsAsync(cancellationToken);
	}

	async Task ReadStreamForwardsAsync(CancellationToken cancellationToken) {
		Log.Information("{streamName} forwards:", data.OrdersStream);

		var events = client.ReadStreamAsync(
			Direction.Forwards, data.OrdersStream, StreamPosition.Start, MaxCount,
			cancellationToken: cancellationToken);

		await foreach (var resolved in events)
			LogEvent(resolved);
	}

	async Task ReadStreamBackwardsAsync(CancellationToken cancellationToken) {
		Log.Information("{streamName} backwards:", data.OrdersStream);

		var events = client.ReadStreamAsync(
			Direction.Backwards, data.OrdersStream, StreamPosition.End, MaxCount,
			cancellationToken: cancellationToken);

		await foreach (var resolved in events)
			LogEvent(resolved);
	}

	async Task ReadAllForwardsAsync(CancellationToken cancellationToken) {
		Log.Information("$all forwards, this run's events only:");

		var events = client.ReadAllAsync(
			Direction.Forwards, Position.Start, StreamFilter.Prefix(data.OrdersStream), MaxCount,
			cancellationToken: cancellationToken);

		await foreach (var resolved in events)
			LogEvent(resolved);
	}

	async Task ReadAllBackwardsAsync(CancellationToken cancellationToken) {
		var events = client.ReadAllAsync(
			direction: Direction.Backwards,
			position: Position.End,
			eventFilter: StreamFilter.None,
			maxCount: 1,
			cancellationToken: cancellationToken);

		await foreach (var resolved in events)
			LogEvent(resolved);
	}

	static void LogEvent(ResolvedEvent resolved) =>
		Log.Information(
			"  #{revision} {eventType} {data}",
			resolved.Event.EventNumber,
			resolved.Event.EventType,
			Encoding.UTF8.GetString(resolved.Event.Data.Span));
}
