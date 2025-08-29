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
		CancellationToken cancellationToken)
		: ReadIndex<ReadIndexEventsForward, ReadIndexEventsForwardCompleted>(bus, indexName, position, maxCount, user, requiresLeader, deadline, cancellationToken) {
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
			replyOnExpired: false,
			expires: Deadline,
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
		CancellationToken cancellationToken)
		: ReadIndex<ReadIndexEventsBackward, ReadIndexEventsBackwardCompleted>(bus, indexName, position, maxCount, user, requiresLeader, deadline, cancellationToken) {
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
			replyOnExpired: false,
			expires: Deadline,
			cancellationToken: CancellationToken);
	}

	public abstract class ReadIndex<TRequest, TResponse> : IAsyncEnumerator<ReadResponse>
		where TRequest : Message
		where TResponse : ReadIndexEventsCompleted {
		protected readonly string IndexName;
		readonly IPublisher _bus;
		protected readonly ulong MaxCount;
		protected readonly ClaimsPrincipal User;
		protected readonly bool RequiresLeader;
		protected readonly DateTime Deadline;
		protected readonly CancellationToken CancellationToken;
		protected readonly SemaphoreSlim Semaphore = new(1, 1);
		readonly Channel<ReadResponse> _channel = Channel.CreateBounded<ReadResponse>(DefaultCatchUpChannelOptions);

		ReadResponse _current;

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
			CancellationToken cancellationToken) {
			_bus = Ensure.NotNull(bus);
			IndexName = Ensure.NotNullOrEmpty(indexName);
			MaxCount = maxCount;
			User = user;
			RequiresLeader = requiresLeader;
			Deadline = deadline;
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
							if (readCount >= MaxCount) {
								_channel.Writer.TryComplete();
								return;
							}

							await _channel.Writer.WriteAsync(new ReadResponse.EventReceived(@event), ct);
							readCount++;
						}

						if (completed.IsEndOfStream || completed.Events.Count == 0) {
							_channel.Writer.TryComplete();
							return;
						}

						var last = completed.Events[^1].EventPosition!.Value;
						ReadPage(Position.FromInt64(last.CommitPosition, last.PreparePosition), true, readCount);
						return;
					default:
						_channel.Writer.TryComplete(ReadResponseException.UnknownError.Create(completed.Result, completed.Error));
						return;
				}
			}
		}
	}
}
