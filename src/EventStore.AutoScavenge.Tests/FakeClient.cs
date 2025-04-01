// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using EventStore.POC.IO.Core;

namespace EventStore.AutoScavenge.Tests;

public class FakeClient : IClient {
	private readonly Dictionary<string, List<Event>> _streams = new();

	public async IAsyncEnumerable<Event> ReadStreamForwards(string stream, long maxCount, [EnumeratorCancellation] CancellationToken ct) {
		if (!_streams.TryGetValue(stream, out var events))
			yield break;

		var count = 0L;
		for (var i = 0; i < events.Count; i++) {
			ct.ThrowIfCancellationRequested();
			yield return events[i];
			count++;

			if (count >= maxCount)
				break;
		}

		await Task.Delay(TimeSpan.Zero, ct);
	}

	public async IAsyncEnumerable<Event> ReadStreamBackwards(string stream, long maxCount, [EnumeratorCancellation] CancellationToken ct) {
		if (!_streams.TryGetValue(stream, out var events))
			yield break;

		var count = 0L;
		for (var i = events.Count - 1; i >= 0; i--) {
			ct.ThrowIfCancellationRequested();
			yield return events[i];
			count++;

			if (count >= maxCount)
				break;
		}

		await Task.Delay(TimeSpan.Zero, ct);
	}

	public void AddEvent(string stream, IEvent @event, ReadOnlyMemory<byte> data) {
		if (!_streams.TryGetValue(stream, out var events)) {
			events = new List<Event>();
			_streams[stream] = events;
		}

		events.Add(new Event(
			eventId: Guid.Empty,
			created: DateTime.Now,
			stream: stream,
			eventNumber: 0,
			eventType: @event.Type,
			contentType: string.Empty,
			commitPosition: 0,
			preparePosition: 0,
			isRedacted: false,
			data: data,
			metadata: Array.Empty<byte>()));
	}

	public Task WriteMetaDataMaxCountAsync(string stream, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public Task<long> WriteAsync(string stream, EventToWrite[] events, long expectedVersion, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public IAsyncEnumerable<Event> SubscribeToAll(FromAll start, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public IAsyncEnumerable<Event> ReadAllBackwardsAsync(Position position, long maxCount, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public Task DeleteStreamAsync(string stream, long expectedVersion, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public IAsyncEnumerable<Event> SubscribeToStream(string stream, CancellationToken cancellationToken) =>
		throw new NotSupportedException();
}
