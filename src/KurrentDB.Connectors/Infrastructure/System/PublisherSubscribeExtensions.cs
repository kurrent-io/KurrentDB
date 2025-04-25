// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using Polly;

namespace KurrentDB.Core;

[PublicAPI]
public static class PublisherSubscribeExtensions {
    static readonly IExpiryStrategy DefaultExpiryStrategy = new DefaultExpiryStrategy();

    const uint DefaultCheckpointIntervalMultiplier = 1;

	static async IAsyncEnumerable<ReadResponse> SubscribeToAll(this IPublisher publisher, Position? position, IEventFilter filter, uint maxSearchWindow, [EnumeratorCancellation] CancellationToken cancellationToken) {
		await using var sub = new Enumerator.AllSubscriptionFiltered(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy,
			checkpoint: position,
			resolveLinks: false,
			eventFilter: filter,
			user: SystemAccounts.System,
			requiresLeader: false,
			maxSearchWindow: maxSearchWindow,
			checkpointIntervalMultiplier: DefaultCheckpointIntervalMultiplier,
			cancellationToken: cancellationToken
		);

		while (!cancellationToken.IsCancellationRequested) {
			if (!await sub.MoveNextAsync(cancellationToken)) // not sure if we need to retry forever or if the enumerator will do that for us
				break;

			yield return sub.Current;
		}
	}

	static async IAsyncEnumerable<ReadResponse> SubscribeToStream(this IPublisher publisher, string stream, StreamRevision? revision, [EnumeratorCancellation] CancellationToken cancellationToken) {
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

			yield return sub.Current;
		}

		yield break;

        static StreamRevision? StartFrom(StreamRevision? revision) => revision == 0 ? null : revision;
	}

	public static Task SubscribeToAll(this IPublisher publisher, Position? position, IEventFilter filter, uint maxSearchWindow, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) {
		_ = Task.Run(async () => {
			var resilienceContext = ResilienceContextPool.Shared
				.Get(nameof(SubscribeToAll), cancellationToken);

			try {
				await resiliencePipeline.ExecuteAsync(
					static async (ctx, state) => {
						await foreach (var response in state.Publisher.SubscribeToAll(state.Checkpoint, state.Filter, state.MaxSearchWindow, ctx.CancellationToken)) {
							// keeping track of the last position before an error occurs
							// so we can let the policy know where to start from
							if (response is ReadResponse.EventReceived eventReceived) {
								state.Checkpoint = new Position(
									(ulong)eventReceived.Event.OriginalPosition!.Value.CommitPosition,
									(ulong)eventReceived.Event.OriginalPosition!.Value.PreparePosition
								);
							}

							await state.Channel.Writer.WriteAsync(response, ctx.CancellationToken);
						}
					},
					resilienceContext, (
						Publisher      : publisher,
						Checkpoint     : position,
						Filter         : filter,
						MaxSearchWindow: maxSearchWindow,
						Channel        : channel
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

	public static Task SubscribeToStream(this IPublisher publisher, StreamRevision? revision, string stream, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) {
		_ = Task.Run(async () => {
			var resilienceContext = ResilienceContextPool.Shared
				.Get(nameof(SubscribeToStream), cancellationToken);

			try {
				await resiliencePipeline.ExecuteAsync(
					static async (ctx, state) => {
                        await foreach (var response in state.Publisher.SubscribeToStream(state.Stream, state.Checkpoint, ctx.CancellationToken)) {
	                        // keeping track of the last position before an error occurs
	                        // so we can let the policy know where to start from
	                        if (response is ReadResponse.EventReceived eventReceived) {
		                        state.Checkpoint = StreamRevision.FromInt64(eventReceived.Event.OriginalEventNumber);
	                        }

	                        await state.Channel.Writer.WriteAsync(response, ctx.CancellationToken);
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
