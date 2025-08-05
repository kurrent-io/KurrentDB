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
	public class ReadAllForwardsFiltered : IAsyncEnumerator<ReadResponse> {
		private static readonly ILogger Log = Serilog.Log.ForContext<ReadAllForwardsFiltered>();

		private readonly IPublisher _bus;
		private readonly ulong _maxCount;
		private readonly bool _resolveLinks;
		private readonly IEventFilter _eventFilter;
		private readonly ClaimsPrincipal _user;
		private readonly bool _requiresLeader;
		private readonly DateTime _deadline;
		private readonly uint _maxSearchWindow;
		private readonly CancellationToken _cancellationToken;
		private readonly SemaphoreSlim _semaphore = new(1, 1);
		private readonly Channel<ReadResponse> _channel = Channel.CreateBounded<ReadResponse>(DefaultCatchUpChannelOptions);

		private ReadResponse _current;

		public ReadResponse Current => _current;

		public ReadAllForwardsFiltered(IPublisher bus,
			Position position,
			ulong maxCount,
			bool resolveLinks,
			IEventFilter eventFilter,
			ClaimsPrincipal user,
			bool requiresLeader,
			uint? maxSearchWindow,
			DateTime deadline,
			CancellationToken cancellationToken) {
			_bus = Ensure.NotNull(bus);
			_maxCount = maxCount;
			_resolveLinks = resolveLinks;
			_eventFilter = Ensure.NotNull(eventFilter);
			_user = user;
			_requiresLeader = requiresLeader;
			_maxSearchWindow = maxSearchWindow ?? DefaultReadBatchSize;
			_deadline = deadline;
			_cancellationToken = cancellationToken;

			ReadPage(position);
		}

		public ValueTask DisposeAsync() {
			_channel.Writer.TryComplete();
			return new ValueTask(Task.CompletedTask);
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

			_bus.Publish(new ClientMessage.FilteredReadAllEventsForward(
				correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, _cancellationToken),
				commitPosition, preparePosition, (int)Math.Min(DefaultReadBatchSize, _maxCount), _resolveLinks,
				_requiresLeader, (int)_maxSearchWindow, null, _eventFilter, _user,
				replyOnExpired: false,
				expires: _deadline,
				cancellationToken: _cancellationToken)
			);

			async Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return;
				}

				if (message is not ClientMessage.FilteredReadAllEventsForwardCompleted completed) {
					_channel.Writer.TryComplete(ReadResponseException.UnknownMessage.Create<ClientMessage.FilteredReadAllEventsForwardCompleted>(message));
					return;
				}

				switch (completed.Result) {
					case FilteredReadAllResult.Success:
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

						ReadPage(Position.FromInt64(
							completed.NextPos.CommitPosition,
							completed.NextPos.PreparePosition), readCount);
						return;
					case FilteredReadAllResult.AccessDenied:
						_channel.Writer.TryComplete(new ReadResponseException.AccessDenied());
						return;
					default:
						_channel.Writer.TryComplete(ReadResponseException.UnknownError.Create(completed.Result, completed.Error));
						return;
				}
			}
		}
	}
}
