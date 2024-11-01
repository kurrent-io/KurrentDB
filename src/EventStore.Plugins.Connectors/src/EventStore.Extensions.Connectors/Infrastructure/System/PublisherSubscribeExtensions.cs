// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using Polly;

namespace EventStore.Core;

[PublicAPI]
public static class PublisherSubscribeExtensions {
    static readonly IExpiryStrategy DefaultExpiryStrategy = new DefaultExpiryStrategy();

    const uint DefaultCheckpointIntervalMultiplier = 1000;

	static async IAsyncEnumerable<ResolvedEvent> SubscribeToAll(this IPublisher publisher, Position? position, IEventFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken) {
		await using var sub = new Enumerator.AllSubscriptionFiltered(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy,
			checkpoint: position,
			resolveLinks: false,
			eventFilter: filter,
			user: SystemAccounts.System,
			requiresLeader: false,
			maxSearchWindow: null,
			checkpointIntervalMultiplier: DefaultCheckpointIntervalMultiplier,
			cancellationToken: cancellationToken
		);

		while (!cancellationToken.IsCancellationRequested) {
			if (!await sub.MoveNextAsync(cancellationToken)) // not sure if we need to retry forever or if the enumerator will do that for us
				break;

			if (sub.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;

            // TODO SS: notify the consumer that we are caught up
            // if (sub.Current is ReadResponse.SubscriptionCaughtUp caughtUp)
            //     yield return caughtUp;
		}
	}

	static async IAsyncEnumerable<ResolvedEvent> SubscribeToStream(this IPublisher publisher, string stream, StreamRevision? revision, [EnumeratorCancellation] CancellationToken cancellationToken) {
		var startRevision = StartFrom(revision);

		await using var sub = new Enumerator.StreamSubscription<string>(
			bus: publisher,
			expiryStrategy: new DefaultExpiryStrategy(), //TODO SS: what is this expiry strategy thing?
			streamName: stream,
			checkpoint: startRevision,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: cancellationToken
		);

		while (!cancellationToken.IsCancellationRequested) {
			if (!await sub.MoveNextAsync()) // not sure if we need to retry forever or if the enumerator will do that for us
				break;

			if (sub.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;

            // if (sub.Current is ReadResponse.SubscriptionCaughtUp caughtUp)
            //     yield return caughtUp;
		}

		yield break;

        static StreamRevision? StartFrom(StreamRevision? revision) => revision == 0 ? null : revision;
	}

	public static Task SubscribeToAll(this IPublisher publisher, Position? position, IEventFilter filter, Channel<ResolvedEvent> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) {
		_ = Task.Run(async () => {
			var resilienceContext = ResilienceContextPool.Shared
				.Get(nameof(SubscribeToAll), cancellationToken);

			try {
				await resiliencePipeline.ExecuteAsync(
					static async (ctx, state) => {
						await foreach (var re in state.Publisher.SubscribeToAll(state.Checkpoint, state.Filter, ctx.CancellationToken)) {
							state.Checkpoint = new Position(
								(ulong)re.OriginalPosition!.Value.CommitPosition,
								(ulong)re.OriginalPosition!.Value.PreparePosition
							);

							await state.Channel.Writer.WriteAsync(re, ctx.CancellationToken);
						}
					},
					resilienceContext, (
						Publisher : publisher,
						Checkpoint: position,
						Filter    : filter,
						Channel   : channel
					)
				);

				channel.Writer.TryComplete();
			}
			catch (OperationCanceledException) {
				channel.Writer.TryComplete();
			}
			catch (Exception ex) {
				channel.Writer.TryComplete(ex);
			}
			finally {
				ResilienceContextPool.Shared
					.Return(resilienceContext);
			}
		}, cancellationToken);

		return Task.CompletedTask;
	}

	public static Task SubscribeToStream(this IPublisher publisher, StreamRevision? revision, string stream, Channel<ResolvedEvent> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) {
		_ = Task.Run(async () => {
			var resilienceContext = ResilienceContextPool.Shared
				.Get(nameof(SubscribeToStream), cancellationToken);

			try {
				await resiliencePipeline.ExecuteAsync(
					static async (ctx, state) => {
                        await foreach (var re in state.Publisher.SubscribeToStream(state.Stream, state.Checkpoint, ctx.CancellationToken)) {
                            state.Checkpoint = StreamRevision.FromInt64(re.OriginalEventNumber);
                            await state.Channel.Writer.WriteAsync(re, ctx.CancellationToken);
                        }
					},
					resilienceContext, (
                        Publisher : publisher,
                        Checkpoint: revision,
                        Channel   : channel,
                        Stream    : stream
					)
				);

				channel.Writer.TryComplete();
			}
			catch (OperationCanceledException) {
				channel.Writer.TryComplete();
			}
			catch (Exception ex) {
				channel.Writer.TryComplete(ex);
			}
			finally {
				ResilienceContextPool.Shared
					.Return(resilienceContext);
			}
		}, cancellationToken);

		return Task.CompletedTask;
	}
}