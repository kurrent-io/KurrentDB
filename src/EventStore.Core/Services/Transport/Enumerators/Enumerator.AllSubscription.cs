// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using Serilog;

namespace EventStore.Core.Services.Transport.Enumerators;

static partial class Enumerator {
	public class AllSubscription : IAsyncEnumerator<ReadResponse> {
		private static readonly ILogger Log = Serilog.Log.ForContext<AllSubscription>();

		private readonly IExpiryStrategy _expiryStrategy;
		private readonly Guid _subscriptionId;
		private readonly IPublisher _bus;
		private readonly bool _resolveLinks;
		private readonly ClaimsPrincipal _user;
		private readonly bool _requiresLeader;
		private readonly CancellationTokenSource _cts;
		private readonly Channel<ReadResponse> _channel;
		private readonly Channel<(ulong SequenceNumber, ResolvedEvent ResolvedEvent)> _liveEvents;

		private ReadResponse _current;
		private bool _disposed;
		private Position? _currentPosition;

		public ReadResponse Current => _current;
		public string SubscriptionId { get; }

		public AllSubscription(
			IPublisher bus,
			IExpiryStrategy expiryStrategy,
			Position? checkpoint,
			bool resolveLinks,
			ClaimsPrincipal user,
			bool requiresLeader,
			CancellationToken cancellationToken) {
			_expiryStrategy = expiryStrategy;
			_subscriptionId = Guid.NewGuid();
			_bus = Ensure.NotNull(bus);
			_resolveLinks = resolveLinks;
			_user = user;
			_requiresLeader = requiresLeader;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_channel = Channel.CreateBounded<ReadResponse>(BoundedChannelOptions);
			_liveEvents = Channel.CreateBounded<(ulong, ResolvedEvent)>(LiveChannelOptions);

			SubscriptionId = _subscriptionId.ToString();

			Subscribe(checkpoint, _cts.Token);
		}

		public ValueTask DisposeAsync() {
			if (_disposed) {
				return ValueTask.CompletedTask;
			}

			Log.Verbose("Subscription {subscriptionId} to $all disposed.", _subscriptionId);

			_disposed = true;
			Unsubscribe();

			_cts.Cancel();
			_cts.Dispose();

			return ValueTask.CompletedTask;
		}

		public async ValueTask<bool> MoveNextAsync() {
			ReadLoop:

			if (!await _channel.Reader.WaitToReadAsync(_cts.Token)) {
				return false;
			}

			var readResponse = await _channel.Reader.ReadAsync(_cts.Token);

			if (readResponse is ReadResponse.EventReceived eventReceived) {
				var eventPos = eventReceived.Event.OriginalPosition!.Value;
				var position = Position.FromInt64(eventPos.CommitPosition, eventPos.PreparePosition);

				if (_currentPosition.HasValue && position <= _currentPosition.Value) {
					// this should no longer happen
					Log.Warning("Subscription {subscriptionId} to $all skipping event {position} as it is less than {currentPosition}.",
						_subscriptionId, position, _currentPosition);
					goto ReadLoop;
				}

				Log.Verbose("Subscription {subscriptionId} to $all seen event {position}.", _subscriptionId, position);

				_currentPosition = position;
			}

			_current = readResponse;

			return true;
		}

		private void Subscribe(Position? checkpoint, CancellationToken ct) {
			Task.Factory.StartNew(() => MainLoop(checkpoint, ct), ct);
		}

		private static TFPos ConvertCheckpoint(Position? checkpoint, TFPos lastLivePos) {
			if (!checkpoint.HasValue)
				return new TFPos(-1, -1);

			if (checkpoint == Position.End)
				return lastLivePos;

			var (commitPos, preparePos) = checkpoint.Value.ToInt64();
			return new TFPos(commitPos, preparePos);
		}

		private async Task MainLoop(Position? checkpointPosition, CancellationToken ct) {
			try {
				Log.Debug("Subscription {subscriptionId} to $all has started at checkpoint {position}",
					_subscriptionId, checkpointPosition?.ToString() ?? "Start");

				var confirmationLastPos = await SubscribeToLive();
				await ConfirmSubscription(ct);

				// the event number we most recently sent on towards the client.
				// (we should send on events _after_ this)
				var checkpoint = ConvertCheckpoint(checkpointPosition, confirmationLastPos);

				// the most recently read sequence number from the live channel. 0 when we haven't read any.
				var sequenceNumber = 0UL;

				if (checkpoint >= confirmationLastPos)
					(checkpoint, sequenceNumber) = await GoLive(checkpoint, sequenceNumber, ct);

				while (true) {
					ct.ThrowIfCancellationRequested();
					checkpoint = await CatchUp(checkpoint, ct);
					(checkpoint, sequenceNumber) = await GoLive(checkpoint, sequenceNumber, ct);
				}
			} catch (Exception ex) {
				if (ex is not (OperationCanceledException or ReadResponseException.InvalidPosition))
					Log.Error(ex, "Subscription {subscriptionId} to $all experienced an error.", _subscriptionId);
				_channel.Writer.TryComplete(ex);
			} finally {
				Log.Debug("Subscription {subscriptionId} to $all has ended.", _subscriptionId);
			}
		}

		private async Task NotifyCaughtUp(TFPos checkpoint, CancellationToken ct) {
			Log.Debug(
				"Subscription {subscriptionId} to $all caught up at checkpoint {position}.",
				_subscriptionId, checkpoint);

			await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionCaughtUp(), ct);
		}

		private async Task NotifyFellBehind(TFPos checkpoint, CancellationToken ct) {
			Log.Debug(
				"Subscription {subscriptionId} to $all fell behind at checkpoint {position}.",
				_subscriptionId, checkpoint);

			await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionFellBehind(), ct);
		}

		private async ValueTask<(TFPos, ulong)> GoLive(TFPos checkpoint, ulong sequenceNumber, CancellationToken ct) {
			await NotifyCaughtUp(checkpoint, ct);

			await foreach (var liveEvent in _liveEvents.Reader.ReadAllAsync(ct)) {
				var sequenceCorrect = liveEvent.SequenceNumber == sequenceNumber + 1;
				sequenceNumber = liveEvent.SequenceNumber;

				if (liveEvent.ResolvedEvent.OriginalPosition <= checkpoint) {
					// skip because this event has already been sent towards the client
					// (or the client specified a checkpoint for events that don't exist yet and so
					// is not interested in them)
					// nb: we can skip this event even if it had the wrong sequence number, it means
					// we would have skipped the events that got discarded anyway.
					continue;
				}

				if (!sequenceCorrect) {
					// there's a gap in the sequence numbers, at least one live event was discarded
					// due to the live channel becoming full.
					// switch back to catchup to make sure we didn't miss anything we wanted to send.
					await NotifyFellBehind(checkpoint, ct);
					return (checkpoint, sequenceNumber);
				}

				// this is the next event to send towards the client. send it and update the checkpoint
				await SendEventToSubscription(liveEvent.ResolvedEvent, ct);
				checkpoint = liveEvent.ResolvedEvent.OriginalPosition!.Value;
			}

			throw new Exception($"Unexpected error: live events channel for subscription {_subscriptionId} to $all completed without exception");
		}

		private Task<TFPos> CatchUp(TFPos checkpoint, CancellationToken ct) {
			Log.Verbose("Subscription {subscriptionId} to $all is catching up from checkpoint {position}", _subscriptionId, checkpoint);

			var catchupCompletionTcs = new TaskCompletionSource<TFPos>();

			// this is a safe use of AsyncTaskEnvelope. Only one call to OnMessage will be running
			// at any given time because we only expect one reply and that reply kicks off the next read.
			AsyncTaskEnvelope envelope = null;
			envelope = new(OnMessage, ct);

			ReadPage(checkpoint, envelope, ct);

			return catchupCompletionTcs.Task;

			async Task OnMessage(Message message, CancellationToken ct) {
				try {
					if (message is ClientMessage.NotHandled notHandled &&
					    TryHandleNotHandled(notHandled, out var ex))
						throw ex;

					if (message is not ClientMessage.ReadAllEventsForwardCompleted completed)
						throw ReadResponseException.UnknownMessage
							.Create<ClientMessage.ReadAllEventsForwardCompleted>(message);

					switch (completed.Result) {
						case ReadAllResult.Success:
							foreach (var @event in completed.Events) {
								var eventPosition = @event.OriginalPosition!.Value;

								// this will only be true for the first event of the first page
								// as we start page reads from the checkpoint's position
								if (eventPosition <= checkpoint)
									continue;

								Log.Verbose("Subscription {subscriptionId} to $all received catch-up event {position}.", _subscriptionId, eventPosition);

								await SendEventToSubscription(@event, ct);
								checkpoint = eventPosition;
							}

							if (completed.IsEndOfStream) {
								catchupCompletionTcs.TrySetResult(checkpoint);
								return;
							}

							ReadPage(completed.NextPos, envelope, ct);
							return;
						case ReadAllResult.Expired:
							ReadPage(completed.CurrentPos, envelope, ct);
							return;
						case ReadAllResult.AccessDenied:
							throw new ReadResponseException.AccessDenied();
						case ReadAllResult.InvalidPosition:
							throw new ReadResponseException.InvalidPosition();
						default:
							throw ReadResponseException.UnknownError.Create(completed.Result);
					}
				} catch (Exception exception) {
					catchupCompletionTcs.TrySetException(exception);
				}
			}
		}

		private async Task SendEventToSubscription(ResolvedEvent @event, CancellationToken ct) {
			await _channel.Writer.WriteAsync(new ReadResponse.EventReceived(@event), ct);
		}

		private Task<TFPos> SubscribeToLive() {
			var nextLiveSequenceNumber = 0UL;
			var confirmationPositionTcs = new TaskCompletionSource<TFPos>();

			_bus.Publish(new ClientMessage.SubscribeToStream(Guid.NewGuid(), _subscriptionId,
				new CallbackEnvelope(OnSubscriptionMessage), _subscriptionId,
				string.Empty, _resolveLinks, _user));

			return confirmationPositionTcs.Task;

			void OnSubscriptionMessage(Message message) {
				try {
					if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex))
						throw ex;

					switch (message) {
						case ClientMessage.SubscriptionConfirmation confirmed:
							long caughtUp = confirmed.LastIndexedPosition;

							Log.Debug(
								"Subscription {subscriptionId} to $all confirmed. LastIndexedPosition is {position:N0}.",
								_subscriptionId, caughtUp);

							confirmationPositionTcs.TrySetResult(new TFPos(caughtUp, caughtUp));
							return;
						case ClientMessage.SubscriptionDropped dropped:
							Log.Debug(
								"Subscription {subscriptionId} to $all dropped by subscription service: {droppedReason}",
								_subscriptionId, dropped.Reason);
							switch (dropped.Reason) {
								case SubscriptionDropReason.AccessDenied:
									throw new ReadResponseException.AccessDenied();
								case SubscriptionDropReason.Unsubscribed:
									return;
								case SubscriptionDropReason.StreamDeleted: // applies only to regular streams
								case SubscriptionDropReason.NotFound: // applies only to persistent subscriptions
								default:
									throw ReadResponseException.UnknownError.Create(dropped.Reason);
							}
						case ClientMessage.StreamEventAppeared appeared: {
							Log.Verbose(
								"Subscription {subscriptionId} to $all received live event {position}.",
								_subscriptionId, appeared.Event.OriginalPosition!.Value);

							if (!_liveEvents.Writer.TryWrite((++nextLiveSequenceNumber, appeared.Event))) {
								// this cannot happen because _liveEvents does not have full mode 'wait'.
								throw new Exception($"Unexpected error: could not write to live events channel for subscription {_subscriptionId} to $all");
							}

							return;
						}
						default:
							throw ReadResponseException.UnknownMessage.Create<ClientMessage.SubscriptionConfirmation>(message);
					}
				} catch (Exception exception) {
					_liveEvents.Writer.TryComplete(exception);
					confirmationPositionTcs.TrySetException(exception);
				}
			}
		}

		private ValueTask ConfirmSubscription(CancellationToken ct) {
			return _channel.Writer.WriteAsync(new ReadResponse.SubscriptionConfirmed(SubscriptionId), ct);
		}

		private void ReadPage(TFPos startPos, IEnvelope envelope, CancellationToken ct) {
			Guid correlationId = Guid.NewGuid();
			Log.Verbose("Subscription {subscriptionId} to $all reading next page starting from {position}.", _subscriptionId, startPos);

			if (startPos is { CommitPosition: < 0, PreparePosition: < 0 })
				startPos = new TFPos(0, 0);

			_bus.Publish(new ClientMessage.ReadAllEventsForward(
				correlationId, correlationId, envelope,
				startPos.CommitPosition, startPos.PreparePosition, ReadBatchSize, _resolveLinks, _requiresLeader, null, _user,
				replyOnExpired: true,
				expires: _expiryStrategy.GetExpiry(),
				cancellationToken: ct));
		}

		private void Unsubscribe() => _bus.Publish(new ClientMessage.UnsubscribeFromStream(Guid.NewGuid(), _subscriptionId, new NoopEnvelope(), _user));
	}
}
