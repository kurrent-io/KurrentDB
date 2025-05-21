// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Data;

namespace KurrentDB.Core.Services.Transport.Enumerators;

partial class Enumerator {
	public class ReadLogEventsSync : IEnumerator<ReadResponse> {
		private readonly IPublisher _bus;
		private readonly ClaimsPrincipal _user;
		private readonly DateTime _deadline;
		private readonly SemaphoreSlim _semaphore;
		private readonly Channel<ReadResponse> _channel;

		private ReadResponse _current;

		public bool MoveNext() {
			while (_channel.Reader.Completion is { IsCompleted: false, IsCanceled: false }) {
				if (!_channel.Reader.TryRead(out _current)) continue;
				return true;
			}

			return false;
		}

		public void Reset() {
			throw new NotSupportedException();
		}

		object IEnumerator.Current {
			get => Current;
		}

		public ReadResponse Current => _current;

		public ReadLogEventsSync(IPublisher bus,
			long[] logPositions,
			ClaimsPrincipal user,
			DateTime deadline) {
			_bus = bus ?? throw new ArgumentNullException(nameof(bus));
			_user = user;
			_deadline = deadline;
			_semaphore = new(1, 1);
			_channel = Channel.CreateBounded<ReadResponse>(BoundedChannelOptions);

			Read(logPositions);
		}

		private void Read(long[] logPositions, ulong readCount = 0) {
			Guid correlationId = Guid.NewGuid();

			_bus.Publish(new ClientMessage.ReadLogEvents(
				correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, CancellationToken.None),
				logPositions, _user, expires: _deadline, cancellationToken: CancellationToken.None));
			return;

			Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return Task.CompletedTask;
				}

				if (message is not ClientMessage.ReadLogEventsCompleted completed) {
					_channel.Writer.TryComplete(ReadResponseException.UnknownMessage
						.Create<ClientMessage.ReadLogEventsCompleted>(message));
					return Task.CompletedTask;
				}

				switch (completed.Result) {
					case ReadEventResult.Success:
						foreach (var @event in completed.Records) {
							while (!_channel.Writer.TryWrite(new ReadResponse.EventReceived(@event))) { }
						}

						_channel.Writer.TryComplete();
						return Task.CompletedTask;
					default:
						_channel.Writer.TryComplete(ReadResponseException.UnknownError.Create(completed.Result));
						return Task.CompletedTask;
				}
			}
		}

		public void Dispose() {
			_semaphore.Dispose();
			_channel.Writer.TryComplete();
		}
	}
}
