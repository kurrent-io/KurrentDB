// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using Polly;

namespace KurrentDB.Core;

[PublicAPI]
public static class PublisherSubscribeExtensions {
	private const uint DefaultCheckpointIntervalMultiplier = 1;

	static async IAsyncEnumerable<ReadResponse> SubscribeToAll(this IPublisher publisher, Position? position, IEventFilter filter, uint maxSearchWindow,
		[EnumeratorCancellation] CancellationToken cancellationToken) {
		await using var sub = new Enumerator.AllSubscriptionFiltered(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy.Instance,
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
			if (!await sub.MoveNextAsync(cancellationToken))
				break;

			yield return sub.Current;
		}
	}

	private static async IAsyncEnumerable<ReadResponse> SubscribeToStream(this IPublisher publisher, string stream, StreamRevision? revision, [EnumeratorCancellation] CancellationToken cancellationToken) {
		var startRevision = StartFrom(revision);

		await using var sub = new Enumerator.StreamSubscription<string>(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			streamName: stream,
			checkpoint: startRevision,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: cancellationToken
		);

		while (!cancellationToken.IsCancellationRequested) {
			if (!await sub.MoveNextAsync())
				break;

			yield return sub.Current;
		}

		yield break;

		static StreamRevision? StartFrom(StreamRevision? revision) =>
			revision == 0 ? null : revision;
	}

	public static async IAsyncEnumerable<ReadResponse> SubscribeToIndex(this IPublisher publisher, string indexName, Position? position, [EnumeratorCancellation] CancellationToken cancellationToken) {
		await using var sub = new Enumerator.IndexSubscription(
			bus: publisher,
			indexName: indexName,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: position,
			user: SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: cancellationToken
		);

		while (!cancellationToken.IsCancellationRequested) {
			if (!await sub.MoveNextAsync(cancellationToken))
				break;

			yield return sub.Current;
		}
	}


	public static Task SubscribeToAll(this IPublisher publisher, Position? position, IEventFilter filter, uint maxSearchWindow, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline,
		CancellationToken cancellationToken) {
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
						Publisher: publisher,
						Checkpoint: position,
						Filter: filter,
						MaxSearchWindow: maxSearchWindow,
						Channel: channel
					)
				);

				channel.Writer.TryComplete();
			} catch (OperationCanceledException) {
				channel.Writer.TryComplete();
			} catch (Exception ex) {
				channel.Writer.TryComplete(ex);
			} finally {
				ResilienceContextPool.Shared
					.Return(resilienceContext);
			}
		}, cancellationToken);

		return Task.CompletedTask;
	}

	public static Task SubscribeToStream(this IPublisher publisher, StreamRevision? revision, string stream, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline,
		CancellationToken cancellationToken) {
		_ = Task.Run(async () => {
			var resilienceContext = ResilienceContextPool.Shared
				.Get(nameof(SubscribeToStream), cancellationToken);

			try {
				await resiliencePipeline.ExecuteAsync(
					static async (ctx, state) => {
						await foreach (var response in state.Publisher.SubscribeToStream(state.Stream, state.Checkpoint, ctx.CancellationToken)) {
							// keeping track of the last position before an error occurs
							// so we can let the policy know where to start from
							if (response is ReadResponse.EventReceived eventReceived)
								state.Checkpoint = StreamRevision.FromInt64(eventReceived.Event.OriginalEventNumber);

							await state.Channel.Writer.WriteAsync(response, ctx.CancellationToken);
						}
					},
					resilienceContext, (
						Publisher: publisher,
						Checkpoint: revision,
						Channel: channel,
						Stream: stream
					)
				);

				channel.Writer.TryComplete();
			} catch (OperationCanceledException) {
				channel.Writer.TryComplete();
			} catch (Exception ex) {
				channel.Writer.TryComplete(ex);
			} finally {
				ResilienceContextPool.Shared
					.Return(resilienceContext);
			}
		}, cancellationToken);

		return Task.CompletedTask;
	}
}
