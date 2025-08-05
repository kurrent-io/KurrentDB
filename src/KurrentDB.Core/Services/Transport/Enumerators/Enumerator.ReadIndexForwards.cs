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
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using Serilog;

namespace KurrentDB.Core.Services.Transport.Enumerators;

partial class Enumerator {
	const int DefaultIndexReadSize = 2000;

	public sealed class ReadIndexForwards : IAsyncEnumerator<ReadResponse> {
		readonly string _indexName;
		readonly IPublisher _bus;
		readonly ulong _maxCount;
		readonly ClaimsPrincipal _user;
		readonly bool _requiresLeader;
		readonly DateTime _deadline;
		readonly CancellationToken _cancellationToken;
		readonly SemaphoreSlim _semaphore = new(1, 1);
		readonly Channel<ReadResponse> _channel = Channel.CreateBounded<ReadResponse>(DefaultCatchUpChannelOptions);

		ReadResponse _current;

		public ReadResponse Current => _current;

		public ReadIndexForwards(
			IPublisher bus,
			string indexName,
			Position position,
			ulong maxCount,
			ClaimsPrincipal user,
			bool requiresLeader,
			DateTime deadline,
			CancellationToken cancellationToken) {
			_bus = Ensure.NotNull(bus);
			_indexName = Ensure.NotNullOrEmpty(indexName);
			_maxCount = maxCount;
			_user = user;
			_requiresLeader = requiresLeader;
			_deadline = deadline;
			_cancellationToken = cancellationToken;

			ReadPage(position);
		}

		public ValueTask DisposeAsync() {
			_channel.Writer.TryComplete();
			return new(Task.CompletedTask);
		}

		public async ValueTask<bool> MoveNextAsync() {
			if (!await _channel.Reader.WaitToReadAsync(_cancellationToken)) {
				return false;
			}

			_current = await _channel.Reader.ReadAsync(_cancellationToken);

			return true;
		}

		private void ReadPage(Position startPosition, ulong readCount = 0) {
			var correlationId = Guid.NewGuid();
			var (commitPosition, preparePosition) = startPosition.ToInt64();

			_bus.Publish(new ClientMessage.ReadIndexEventsForward(
				correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, _cancellationToken),
				_indexName, commitPosition, preparePosition, (int)Math.Min(DefaultIndexReadSize, _maxCount),
				_requiresLeader, null, _user,
				replyOnExpired: false,
				expires: _deadline,
				cancellationToken: _cancellationToken)
			);

			async Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return;
				}

				if (message is not ClientMessage.ReadIndexEventsForwardCompleted completed) {
					_channel.Writer.TryComplete(ReadResponseException.UnknownMessage.Create<ClientMessage.FilteredReadAllEventsForwardCompleted>(message));
					return;
				}

				switch (completed.Result) {
					case ReadIndexResult.Success:
						foreach (var @event in completed.Events) {
							if (readCount >= _maxCount) {
								_channel.Writer.TryComplete();
								return;
							}

							await _channel.Writer.WriteAsync(new ReadResponse.EventReceived(@event), ct);
							readCount++;
						}

						if (completed.IsEndOfStream) {
							_channel.Writer.TryComplete();
							return;
						}

						ReadPage(Position.FromInt64(completed.NextPos.CommitPosition, completed.NextPos.PreparePosition), readCount);
						return;
					default:
						_channel.Writer.TryComplete(ReadResponseException.UnknownError.Create(completed.Result, completed.Error));
						return;
				}
			}
		}
	}
}
