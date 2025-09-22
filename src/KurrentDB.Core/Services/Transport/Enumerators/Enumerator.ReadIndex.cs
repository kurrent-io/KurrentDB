// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Transport.Enumerators;

partial class Enumerator {
	const int DefaultIndexReadSize = 2000;

	public sealed class ReadIndexForwards(
		IPublisher bus,
		string indexName,
		Position position,
		ulong maxCount,
		ClaimsPrincipal user,
		bool requiresLeader,
		DateTime deadline,
		IExpiryStrategy expiryStrategy,
		CancellationToken cancellationToken)
		: ReadIndex<ReadIndexEventsForward, ReadIndexEventsForwardCompleted>(bus, indexName, position, maxCount, user, requiresLeader, deadline, expiryStrategy, cancellationToken) {
		protected override ReadIndexEventsForward CreateRequest(
			Guid correlationId,
			long commitPosition,
			long preparePosition,
			bool excludeStart,
			Func<Message, CancellationToken, Task> onMessage
		) => new(
			correlationId, correlationId, new ContinuationEnvelope(onMessage, Semaphore, CancellationToken),
			IndexName, commitPosition, preparePosition, excludeStart, (int)Math.Min(DefaultIndexReadSize, MaxCount),
			RequiresLeader, null, User,
			replyOnExpired: true,
			expires: ExpiryStrategy.GetExpiry() ?? Deadline,
			cancellationToken: CancellationToken);
	}

	public sealed class ReadIndexBackwards(
		IPublisher bus,
		string indexName,
		Position position,
		ulong maxCount,
		ClaimsPrincipal user,
		bool requiresLeader,
		DateTime deadline,
		IExpiryStrategy expiryStrategy,
		CancellationToken cancellationToken)
		: ReadIndex<ReadIndexEventsBackward, ReadIndexEventsBackwardCompleted>(bus, indexName, position, maxCount, user, requiresLeader, deadline, expiryStrategy, cancellationToken) {
		protected override ReadIndexEventsBackward CreateRequest(
			Guid correlationId,
			long commitPosition,
			long preparePosition,
			bool excludeStart,
			Func<Message, CancellationToken, Task> onMessage
		) => new(
			correlationId, correlationId, new ContinuationEnvelope(onMessage, Semaphore, CancellationToken),
			IndexName, commitPosition, preparePosition, excludeStart, (int)Math.Min(DefaultIndexReadSize, MaxCount),
			RequiresLeader, null, User,
			replyOnExpired: true,
			expires: ExpiryStrategy.GetExpiry() ?? Deadline,
			cancellationToken: CancellationToken);
	}

	public abstract class ReadIndex<TRequest, TResponse> : IAsyncEnumerator<ReadResponse>
		where TRequest : Message
		where TResponse : ReadIndexEventsCompleted {
		protected readonly string IndexName;
		protected readonly ulong MaxCount;
		protected readonly ClaimsPrincipal User;
		protected readonly bool RequiresLeader;
		protected readonly DateTime Deadline;
		protected readonly IExpiryStrategy ExpiryStrategy;
		protected readonly CancellationToken CancellationToken;
		protected readonly SemaphoreSlim Semaphore = new(1, 1);
		private readonly IPublisher _bus;
		private readonly Channel<ReadResponse> _channel = Channel.CreateBounded<ReadResponse>(DefaultCatchUpChannelOptions);

		private ReadResponse _current;

		public ReadResponse Current => _current;

		protected abstract TRequest CreateRequest(
			Guid correlationId,
			long commitPosition,
			long preparePosition,
			bool excludeStart,
			Func<Message, CancellationToken, Task> onMessage
		);

		protected ReadIndex(
			IPublisher bus,
			string indexName,
			Position position,
			ulong maxCount,
			ClaimsPrincipal user,
			bool requiresLeader,
			DateTime deadline,
			IExpiryStrategy expiryStrategy,
			CancellationToken cancellationToken) {
			_bus = Ensure.NotNull(bus);
			IndexName = Ensure.NotNullOrEmpty(indexName);
			MaxCount = maxCount;
			User = user;
			RequiresLeader = requiresLeader;
			Deadline = deadline;
			ExpiryStrategy = expiryStrategy;
			CancellationToken = cancellationToken;

			ReadPage(position, false);
		}

		public ValueTask DisposeAsync() {
			_channel.Writer.TryComplete();
			return new(Task.CompletedTask);
		}

		public async ValueTask<bool> MoveNextAsync() {
			if (!await _channel.Reader.WaitToReadAsync(CancellationToken)) {
				return false;
			}

			_current = await _channel.Reader.ReadAsync(CancellationToken);

			return true;
		}

		private void ReadPage(Position startPosition, bool excludeStart, ulong readCount = 0) {
			var correlationId = Guid.NewGuid();
			var (commitPosition, preparePosition) = startPosition.ToInt64();

			_bus.Publish(CreateRequest(correlationId, commitPosition, preparePosition, excludeStart, OnMessage));

			async Task OnMessage(Message message, CancellationToken ct) {
				if (message is NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return;
				}

				if (message is not TResponse completed) {
					_channel.Writer.TryComplete(ReadResponseException.UnknownMessage.Create<TResponse>(message));
					return;
				}

				switch (completed.Result) {
					case ReadIndexResult.Success:
						foreach (var @event in completed.Events) {
							await _channel.Writer.WriteAsync(new ReadResponse.EventReceived(@event), ct);

							if (++readCount >= MaxCount) {
								_channel.Writer.TryComplete();
								return;
							}
						}

						if (completed.IsEndOfStream || completed.Events.Count == 0) {
							_channel.Writer.TryComplete();
							return;
						}

						var last = completed.Events[^1].EventPosition!.Value;
						ReadPage(Position.FromInt64(last.CommitPosition, last.PreparePosition), true, readCount);
						return;
					case ReadIndexResult.Expired:
						ReadPage(Position.FromInt64(completed.CurrentPos.CommitPosition, completed.CurrentPos.PreparePosition), excludeStart, readCount);
						return;
				}

				Exception exception = completed.Result switch {
					ReadIndexResult.AccessDenied => new ReadResponseException.AccessDenied(),
					ReadIndexResult.IndexNotFound => new ReadResponseException.IndexNotFound(IndexName),
					ReadIndexResult.InvalidPosition => new ReadResponseException.InvalidPosition(),
					_ => ReadResponseException.UnknownError.Create(completed.Result, completed.Error)
				};
				_channel.Writer.TryComplete(exception);
			}
		}
	}
}
