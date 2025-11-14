// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.Core.ClientPublisher;

[PublicAPI]
public static class PublisherReadExtensions {
	public static async IAsyncEnumerable<ResolvedEvent> Read(this IPublisher publisher, Position startPosition, IEventFilter filter, long maxCount, bool forwards = true, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
		await using var enumerator = GetEnumerator();

		while (!cancellationToken.IsCancellationRequested) {
			if (!await enumerator.MoveNextAsync())
				break;

			if (enumerator.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;
		}

		yield break;

		IAsyncEnumerator<ReadResponse> GetEnumerator() {
			return forwards
				? new Enumerator.ReadAllForwardsFiltered(
					bus: publisher,
					position: startPosition,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					eventFilter: filter,
					user: SystemAccounts.System,
					requiresLeader: false,
					maxSearchWindow: null,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken
				)
				: new Enumerator.ReadAllBackwardsFiltered(
					bus: publisher,
					position: startPosition,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					eventFilter: filter,
					user: SystemAccounts.System,
					requiresLeader: false,
					maxSearchWindow: null,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken
				);
		}
	}

	public static IAsyncEnumerable<ResolvedEvent> ReadForwards(this IPublisher publisher, Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.Read(startPosition, filter, maxCount, true, cancellationToken);

	public static IAsyncEnumerable<ResolvedEvent> ReadBackwards(this IPublisher publisher, Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.Read(startPosition, filter, maxCount, false, cancellationToken);

	public static async IAsyncEnumerable<ResolvedEvent> Read(this IPublisher publisher, Position startPosition, long maxCount, bool forwards = true, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
		await using var enumerator = GetEnumerator();

		while (!cancellationToken.IsCancellationRequested) {
			if (!await enumerator.MoveNextAsync())
				break;

			if (enumerator.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;
		}

		yield break;

		IAsyncEnumerator<ReadResponse> GetEnumerator() {
			return forwards
				? new Enumerator.ReadAllForwards(
					bus: publisher,
					position: startPosition,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					user: SystemAccounts.System,
					requiresLeader: false,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken
				)
				: new Enumerator.ReadAllBackwards(
					bus: publisher,
					position: startPosition,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					user: SystemAccounts.System,
					requiresLeader: false,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken
				);
		}
	}

	public static IAsyncEnumerable<ResolvedEvent> ReadForwards(this IPublisher publisher, Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.Read(startPosition, maxCount, true, cancellationToken);

	public static IAsyncEnumerable<ResolvedEvent> ReadBackwards(this IPublisher publisher, Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.Read(startPosition, maxCount, false, cancellationToken);

	public static async ValueTask<ResolvedEvent?> ReadFirstEvent(this IPublisher publisher, CancellationToken cancellationToken = default) {
		var first = await publisher.Read(
			Position.Start, EventFilter.Unfiltered, 1,
			true, cancellationToken
		).FirstOrDefaultAsync(cancellationToken);

		return first == ResolvedEvent.EmptyEvent ? null : first;
	}

	public static async ValueTask<ResolvedEvent?> ReadLastEvent(this IPublisher publisher, CancellationToken cancellationToken = default) {
		var last = await publisher.Read(
			Position.End, EventFilter.Unfiltered, 1,
			false, cancellationToken
		).FirstOrDefaultAsync(cancellationToken);

		return last == ResolvedEvent.EmptyEvent ? null : last;
	}

	public static async IAsyncEnumerable<ResolvedEvent> ReadStream(this IPublisher publisher, string stream, StreamRevision startRevision, long maxCount, bool forwards, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
		await using var enumerator = GetEnumerator();

		while (!cancellationToken.IsCancellationRequested) {
			if (!await enumerator.MoveNextAsync())
				break;

			// if (enumerator.Current is ReadResponse.LastStreamPositionReceived lastStreamPositionReceived)
			//     break;

			if (enumerator.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;

			if (enumerator.Current is ReadResponse.StreamNotFound streamNotFound)
				throw new ReadResponseException.StreamNotFound(streamNotFound.StreamName);
		}

		yield break;

		IAsyncEnumerator<ReadResponse> GetEnumerator() {
			return forwards
				? new Enumerator.ReadStreamForwards(
					bus: publisher,
					streamName: stream,
					startRevision: startRevision,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					user: SystemAccounts.System,
					requiresLeader: false,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken,
					compatibility: 1 // whats this?
				)
				: new Enumerator.ReadStreamBackwards(
					bus: publisher,
					streamName: stream,
					startRevision: startRevision,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					user: SystemAccounts.System,
					requiresLeader: false,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken,
					compatibility: 1 // whats this?
				);
		}
	}

	public static IAsyncEnumerable<ResolvedEvent> ReadStreamForwards(this IPublisher publisher, string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default) =>
		 publisher.ReadStream(stream, startRevision, maxCount, true, cancellationToken);

	public static IAsyncEnumerable<ResolvedEvent> ReadFullStream(this IPublisher publisher, string stream, CancellationToken cancellationToken = default) =>
		publisher.ReadStream(stream, StreamRevision.Start, long.MaxValue, true, cancellationToken);

	public static IAsyncEnumerable<ResolvedEvent> ReadStreamBackwards(this IPublisher publisher, string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.ReadStream(stream, startRevision, maxCount, false, cancellationToken);

	public static async IAsyncEnumerable<ResolvedEvent> ReadStreamByPosition(this IPublisher publisher, Position startPosition, long maxCount, bool forwards, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
		var result = await publisher.GetStreamInfoByPosition(startPosition, cancellationToken);

		if (result is null)
			throw new("Stream not found by position");

		await foreach (var re in publisher.ReadStream(result.Value.Stream, result.Value.Revision, maxCount, forwards, cancellationToken))
			yield return re;
	}

	public static IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionForwards(this IPublisher publisher, Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.ReadStreamByPosition(startPosition, maxCount, true, cancellationToken);

	public static IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionBackwards(this IPublisher publisher, Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.ReadStreamByPosition(startPosition, maxCount, false, cancellationToken);

	public static async Task<ResolvedEvent?> ReadStreamLastEvent(this IPublisher publisher, string stream, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		var last = await publisher
			.ReadStreamBackwards(stream, StreamRevision.End, 1, cancellationToken)
			.FirstOrDefaultAsync(cancellationToken);

		return last == ResolvedEvent.EmptyEvent ? null : last;
	}

	public static async Task<ResolvedEvent?> ReadStreamFirstEvent(this IPublisher publisher, string stream, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		var first = await publisher
			.ReadStreamForwards(stream, StreamRevision.Start, 1, cancellationToken)
			.FirstOrDefaultAsync(cancellationToken);

		return first == ResolvedEvent.EmptyEvent ? null : first;
	}

	public static async Task<ResolvedEvent> ReadEvent(this IPublisher publisher, Position position, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		var result = await publisher
			.ReadForwards(position, 1, cancellationToken)
			.FirstOrDefaultAsync(cancellationToken);

		return result;
	}

	public static async IAsyncEnumerable<ResolvedEvent> ReadIndex(this IPublisher publisher, string indexName, Position startPosition, long maxCount, bool forwards = true, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
		await using var enumerator = GetEnumerator();

		while (!cancellationToken.IsCancellationRequested) {
			if (!await enumerator.MoveNextAsync())
				break;

			if (enumerator.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;
		}

		yield break;

		IAsyncEnumerator<ReadResponse> GetEnumerator() {
			return forwards
				? new Enumerator.ReadIndexForwards(
					bus: publisher,
					indexName: indexName,
					position: startPosition,
					maxCount: (ulong)maxCount,
					user: SystemAccounts.System,
					requiresLeader: false,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken
				)
				: new Enumerator.ReadIndexBackwards(
					bus: publisher,
					indexName: indexName,
					position: startPosition,
					maxCount: (ulong)maxCount,
					user: SystemAccounts.System,
					requiresLeader: false,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					cancellationToken: cancellationToken
				);
		}
	}
}
