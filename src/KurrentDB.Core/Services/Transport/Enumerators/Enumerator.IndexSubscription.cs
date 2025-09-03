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
using Serilog;
using static KurrentDB.Core.Messages.ClientMessage;
using TFPos = KurrentDB.Core.Data.TFPos;

namespace KurrentDB.Core.Services.Transport.Enumerators;

partial class Enumerator {
	public sealed class IndexSubscription : IAsyncEnumerator<ReadResponse> {
		static readonly ILogger Log = Serilog.Log.ForContext<IndexSubscription>();

		readonly IExpiryStrategy _expiryStrategy;
		readonly Guid _subscriptionId;
		readonly IPublisher _bus;
		readonly ClaimsPrincipal _user;
		readonly bool _requiresLeader;
		readonly CancellationTokenSource _cts;
		readonly Channel<ReadResponse> _channel;
		readonly Channel<(ulong SequenceNumber, ResolvedEvent? ResolvedEvent, TFPos? Checkpoint)> _liveEvents;

		bool _disposed;
		readonly string _indexName;

		public ReadResponse Current { get; private set; }

		string SubscriptionId { get; }

		public IndexSubscription(IPublisher bus,
			IExpiryStrategy expiryStrategy,
			Position? checkpoint,
			string indexName,
			ClaimsPrincipal user,
			bool requiresLeader,
			CancellationToken cancellationToken) {
			_expiryStrategy = expiryStrategy;
			_subscriptionId = Guid.NewGuid();
			_bus = Ensure.NotNull(bus);
			_indexName = Ensure.NotNullOrEmpty(indexName);
			_user = user;
			_requiresLeader = requiresLeader;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_channel = Channel.CreateBounded<ReadResponse>(DefaultCatchUpChannelOptions);
			_liveEvents = Channel.CreateBounded<(ulong, ResolvedEvent?, TFPos?)>(DefaultLiveChannelOptions);

			SubscriptionId = _subscriptionId.ToString();

			// Subscribe
			Task.Factory.StartNew(() => MainLoop(checkpoint, _cts.Token), _cts.Token);
		}

		async Task MainLoop(Position? checkpointPosition, CancellationToken ct) {
			try {
				Log.Debug("Subscription {SubscriptionId} to {IndexName} has started at checkpoint {Position}",
					_subscriptionId, _indexName, checkpointPosition?.ToString() ?? "Start");

				var confirmationLastPos = await SubscribeToLive();
				await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionConfirmed(SubscriptionId), ct);

				// the event or checkpoint position we most recently sent on towards the client.
				// (we should send on events _after_ this and checkpoints _equal to or after_ this)
				var checkpoint = ConvertCheckpoint(checkpointPosition, confirmationLastPos);

				// the most recently read sequence number from the live channel. 0 when we haven't read any.
				var sequenceNumber = 0UL;

				if (checkpoint >= confirmationLastPos) {
					(checkpoint, sequenceNumber) = await GoLive(checkpoint, sequenceNumber, ct);
				}

				while (true) {
					ct.ThrowIfCancellationRequested();
					checkpoint = await CatchUp(checkpoint, ct);
					(checkpoint, sequenceNumber) = await GoLive(checkpoint, sequenceNumber, ct);
				}
			} catch (Exception ex) {
				if (ex is not (OperationCanceledException or ReadResponseException.InvalidPosition)) {
					Log.Error(ex, "Subscription {SubscriptionId} to {IndexName} experienced an error.", _subscriptionId, _indexName);
				}

				_channel.Writer.TryComplete(ex);
			} finally {
				Log.Debug("Subscription {SubscriptionId} to {IndexName} has ended.", _subscriptionId, _indexName);
			}

			return;

			static TFPos ConvertCheckpoint(Position? checkpoint, TFPos lastLivePos) {
				if (!checkpoint.HasValue)
					return TFPos.HeadOfTf;

				if (checkpoint == Position.End)
					return lastLivePos;

				var (commitPos, preparePos) = checkpoint.Value.ToInt64();
				return new(commitPos, preparePos);
			}
		}

		public async ValueTask<bool> MoveNextAsync() {
			if (!await _channel.Reader.WaitToReadAsync(_cts.Token)) {
				return false;
			}

			var readResponse = await _channel.Reader.ReadAsync(_cts.Token);

			if (readResponse is ReadResponse.EventReceived eventReceived) {
				var eventPos = eventReceived.Event.OriginalPosition!.Value;
				var position = Position.FromInt64(eventPos.CommitPosition, eventPos.PreparePosition);
				Log.Verbose("Subscription {SubscriptionId} to {IndexName} seen event {Position}.", _subscriptionId, _indexName, position);
			}

			Current = readResponse;

			return true;
		}

		async ValueTask<(TFPos, ulong)> GoLive(TFPos checkpoint, ulong sequenceNumber, CancellationToken ct) {
			await NotifyCaughtUp(checkpoint);

			await foreach (var liveEvent in _liveEvents.Reader.ReadAllAsync(ct)) {
				var sequenceCorrect = liveEvent.SequenceNumber == sequenceNumber + 1;
				sequenceNumber = liveEvent.SequenceNumber;

				if (liveEvent.ResolvedEvent.HasValue && liveEvent.ResolvedEvent.Value.OriginalPosition <= checkpoint) {
					continue;
				}

				if (liveEvent.Checkpoint.HasValue && liveEvent.Checkpoint.Value < checkpoint) {
					// skip because the checkpoint received is earlier than the last event or checkpoint sent towards the client
					continue;
				}

				if (!sequenceCorrect) {
					// there's a gap in the sequence numbers, at least one live event was discarded
					// due to the live channel becoming full.
					// switch back to catch up to make sure we didn't miss anything we wanted to send.
					await NotifyFellBehind(checkpoint);

					return (checkpoint, sequenceNumber);
				}

				if (liveEvent.ResolvedEvent.HasValue) {
					// this is the next event to send towards the client. send it and update `checkpoint`
					await SendEventToSubscription(liveEvent.ResolvedEvent.Value, ct);
					checkpoint = liveEvent.ResolvedEvent.Value.OriginalPosition!.Value;
				} else if (liveEvent.Checkpoint.HasValue) {
					checkpoint = liveEvent.Checkpoint.Value;
				}
			}

			throw new($"Unexpected error: live events channel for subscription {_subscriptionId} to {_indexName} completed without exception");

			async Task NotifyCaughtUp(TFPos tfPos) {
				Log.Debug("Subscription {SubscriptionId} to {IndexName} caught up at checkpoint {Position}.", _subscriptionId, _indexName, tfPos);

				await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionCaughtUp(DateTime.UtcNow, tfPos), ct);
			}

			async Task NotifyFellBehind(TFPos tfPos) {
				Log.Debug("Subscription {SubscriptionId} to {IndexName} fell behind at checkpoint {Position}.", _subscriptionId, _indexName, tfPos);

				await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionFellBehind(DateTime.UtcNow, tfPos), ct);
			}
		}

		Task<TFPos> CatchUp(TFPos checkpoint, CancellationToken ct) {
			Log.Verbose("Subscription {SubscriptionId} to {IndexName} is catching up from checkpoint {Position}", _subscriptionId, _indexName, checkpoint);

			var catchupCompletionTcs = new TaskCompletionSource<TFPos>();

			// this is a safe use of AsyncTaskEnvelope. Only one call to OnMessage will be running
			// at any given time because we only expect one reply and that reply kicks off the next read.
			AsyncTaskEnvelope envelope = null;
			envelope = new(OnMessage, ct);

			ReadPage(checkpoint, envelope, ct);

			return catchupCompletionTcs.Task;

			async Task OnMessage(Message message, CancellationToken ct) {
				try {
					if (message is NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
						throw ex;
					}

					if (message is not ReadIndexEventsForwardCompleted completed)
						throw ReadResponseException.UnknownMessage.Create<ReadIndexEventsForwardCompleted>(message);

					switch (completed.Result) {
						case ReadIndexResult.Success:
							foreach (var @event in completed.Events) {
								var eventPosition = @event.OriginalPosition!.Value;

								// this can only be true for the first event of the first page
								// as we start page reads from the checkpoint's position
								if (eventPosition <= checkpoint)
									continue;

								Log.Verbose("Subscription {SubscriptionId} to {IndexName} received catch-up event {Position}.", _subscriptionId, _indexName, eventPosition);

								await SendEventToSubscription(@event, ct);
								checkpoint = eventPosition;
							}

							if (completed.IsEndOfStream) {
								catchupCompletionTcs.TrySetResult(checkpoint);
								return;
							}

							ReadPage(completed.Events[^1].EventPosition!.Value, envelope, ct);
							return;
						case ReadIndexResult.Expired:
							ReadPage(completed.CurrentPos, envelope, ct);
							return;
						case ReadIndexResult.InvalidPosition:
							throw new ReadResponseException.InvalidPosition();
						default:
							throw ReadResponseException.UnknownError.Create(completed.Result, completed.Error);
					}
				} catch (Exception exception) {
					catchupCompletionTcs.TrySetException(exception);
				}
			}
		}

		ValueTask SendEventToSubscription(ResolvedEvent @event, CancellationToken ct) => _channel.Writer.WriteAsync(new ReadResponse.EventReceived(@event), ct);

		Task<TFPos> SubscribeToLive() {
			var nextLiveSequenceNumber = 0UL;
			var confirmationPositionTcs = new TaskCompletionSource<TFPos>();

			_bus.Publish(new SubscribeToIndex(Guid.NewGuid(), _subscriptionId, new CallbackEnvelope(OnSubscriptionMessage), _subscriptionId, _indexName, _user));

			return confirmationPositionTcs.Task;

			void OnSubscriptionMessage(Message message) {
				try {
					if (message is NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
						throw ex;
					}

					switch (message) {
						case SubscriptionConfirmation confirmed:
							long caughtUp = confirmed.LastIndexedPosition;

							Log.Debug("Subscription {SubscriptionId} to {IndexName} confirmed. LastIndexedPosition is {Position:N0}.", _subscriptionId, _indexName, caughtUp);

							confirmationPositionTcs.TrySetResult(new TFPos(caughtUp, caughtUp));
							return;
						case SubscriptionDropped dropped:
							Log.Debug("Subscription {SubscriptionId} to {IndexName} dropped by subscription service: {DroppedReason}", _subscriptionId, _indexName, dropped.Reason);
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
						case StreamEventAppeared appeared:
							Log.Verbose("Subscription {SubscriptionId} to {IndexName} received live event {Position}.", _subscriptionId, _indexName, appeared.Event.OriginalPosition!.Value);

							if (!_liveEvents.Writer.TryWrite((++nextLiveSequenceNumber, appeared.Event, null))) {
								// this cannot happen because _liveEvents does not have full mode 'wait'.
								throw new($"Unexpected error: could not write to live events channel for subscription {_subscriptionId} to {_indexName}");
							}

							return;
						case CheckpointReached checkpointReached:
							Log.Verbose("Subscription {SubscriptionId} to {IndexName} received live checkpoint {Position}.", _subscriptionId, _indexName, checkpointReached.Position);

							if (!_liveEvents.Writer.TryWrite((++nextLiveSequenceNumber, null, checkpointReached.Position!.Value))) {
								// this cannot happen because _liveEvents does not have full mode 'wait'.
								throw new($"Unexpected error: could not write to live events channel for subscription {_subscriptionId} to {_indexName}");
							}

							return;
						default:
							throw ReadResponseException.UnknownMessage.Create<SubscriptionConfirmation>(message);
					}
				} catch (Exception exception) {
					_liveEvents.Writer.TryComplete(exception);
					confirmationPositionTcs.TrySetException(exception);
				}
			}
		}

		void ReadPage(TFPos startPos, IEnvelope envelope, CancellationToken ct) {
			Guid correlationId = Guid.NewGuid();
			Log.Verbose("Subscription {SubscriptionId} to {IndexName} reading next page starting from {Position}.", _subscriptionId, _indexName, startPos);

			if (startPos is { CommitPosition: < 0, PreparePosition: < 0 })
				startPos = new TFPos(0, 0);

			_bus.Publish(new ReadIndexEventsForward(
				internalCorrId: correlationId,
				correlationId: correlationId,
				envelope: envelope,
				indexName: _indexName,
				commitPosition: startPos.CommitPosition,
				preparePosition: startPos.PreparePosition,
				excludeStart: true,
				maxCount: DefaultIndexReadSize,
				requireLeader: _requiresLeader,
				validationTfLastCommitPosition: null,
				user: _user,
				replyOnExpired: true,
				expires: _expiryStrategy.GetExpiry(),
				cancellationToken: ct));
		}

		public ValueTask DisposeAsync() {
			if (_disposed) {
				return ValueTask.CompletedTask;
			}

			Log.Verbose("Subscription {SubscriptionId} to{IndexName} disposed.", _subscriptionId, _indexName);

			_disposed = true;
			Unsubscribe();

			_cts.Cancel();
			_cts.Dispose();

			return ValueTask.CompletedTask;

			void Unsubscribe() => _bus.Publish(new UnsubscribeFromStream(Guid.NewGuid(), _subscriptionId, new NoopEnvelope(), _user));
		}
	}
}
