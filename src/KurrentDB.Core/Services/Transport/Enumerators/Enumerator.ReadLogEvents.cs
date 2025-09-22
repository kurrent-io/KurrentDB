// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.Transport.Enumerators;

partial class Enumerator {
	/// <summary>
	/// This is a synchronous enumerator that reads the log events in a blocking manner.
	/// It is ONLY used in the kdb_get DuckDB inline function.
	/// Don't use it anywhere else.
	/// </summary>
	public sealed class ReadLogEventsSync : IEnumerator<ReadResponse> {
		private readonly IPublisher _bus;
		private readonly ClaimsPrincipal _user;
		private readonly SemaphoreSlim _semaphore;
		private readonly Channel<ReadResponse> _channel;

		private ReadResponse _current;

		public bool MoveNext() {
			while (_channel.Reader.Completion is { IsCompleted: false, IsCanceled: false }) {
				if (!_channel.Reader.TryRead(out _current)) {
					Thread.Sleep(1);
					continue;
				}

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

		public ReadLogEventsSync(IPublisher bus, long[] logPositions, ClaimsPrincipal user) {
			_bus = bus ?? throw new ArgumentNullException(nameof(bus));
			_user = user;
			_semaphore = new(1, 1);
			_channel = Channel.CreateBounded<ReadResponse>(DefaultCatchUpChannelOptions);

			Read(logPositions);
		}

		private void Read(long[] logPositions) {
			Guid correlationId = Guid.NewGuid();

			_bus.Publish(new ClientMessage.ReadLogEvents(
				correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, CancellationToken.None),
				logPositions, _user, replyOnExpired: true, expires: null, cancellationToken: CancellationToken.None));
			return;

			Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return Task.CompletedTask;
				}

				if (message is not ClientMessage.ReadLogEventsCompleted completed) {
					_channel.Writer.TryComplete(ReadResponseException.UnknownMessage.Create<ClientMessage.ReadLogEventsCompleted>(message));
					return Task.CompletedTask;
				}

				switch (completed.Result) {
					case ReadEventResult.Success:
						foreach (var @event in completed.Records) {
							while (!_channel.Writer.TryWrite(new ReadResponse.EventReceived(@event))) {
								Thread.Sleep(1);
							}
						}

						_channel.Writer.TryComplete();
						return Task.CompletedTask;
					case ReadEventResult.Expired:
						Read(logPositions);
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
