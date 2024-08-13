// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;

namespace EventStore.Core;

[PublicAPI]
public static class PublisherReadExtensions {
	//TODO SS: what should I do with this deadline?
	public static readonly DateTime DefaultDeadline = DateTime.UtcNow.AddYears(1);

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
                    deadline: DefaultDeadline,
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
                    deadline: DefaultDeadline,
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
					deadline: DefaultDeadline,
					cancellationToken: cancellationToken
				)
				: new Enumerator.ReadAllBackwards(
					bus: publisher,
					position: startPosition,
					maxCount: (ulong)maxCount,
					resolveLinks: false,
					user: SystemAccounts.System,
					requiresLeader: false,
					deadline: DefaultDeadline,
					cancellationToken: cancellationToken
				);
		}
	}

	public static IAsyncEnumerable<ResolvedEvent> ReadForwards(this IPublisher publisher, Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.Read(startPosition, maxCount, true, cancellationToken);

	public static IAsyncEnumerable<ResolvedEvent> ReadBackwards(this IPublisher publisher, Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
		publisher.Read(startPosition, maxCount, false, cancellationToken);

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
					deadline: DefaultDeadline,
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
					deadline: DefaultDeadline,
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
			throw new Exception("Stream not found by position");

		await foreach(var re in publisher.ReadStream(result.Value.Stream, result.Value.Revision, maxCount, forwards, cancellationToken))
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
}