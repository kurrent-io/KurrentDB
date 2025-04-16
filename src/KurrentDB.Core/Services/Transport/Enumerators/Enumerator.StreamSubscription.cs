// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Messages;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using Serilog;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Core.Services.Transport.Enumerators;

static partial class Enumerator {
	public abstract class StreamSubscription : IAsyncEnumerator<ReadResponse> {
		protected static readonly ILogger Log = Serilog.Log.ForContext<StreamSubscription>();
		public abstract ValueTask DisposeAsync();
		public abstract ValueTask<bool> MoveNextAsync();
		public abstract ReadResponse Current { get; }
	}

	public class StreamSubscription<TStreamId> : StreamSubscription {
		private readonly IExpiryStrategy _expiryStrategy;
		private readonly Guid _subscriptionId;
		private readonly IPublisher _bus;
		private readonly string _streamName;
		private readonly bool _resolveLinks;
		private readonly ClaimsPrincipal _user;
		private readonly bool _requiresLeader;
		private readonly CancellationTokenSource _cts;
		private readonly Channel<ReadResponse> _channel;
		private readonly Channel<(ulong SequenceNumber, ResolvedEvent ResolvedEvent)> _liveEvents;

		private ReadResponse _current;
		private bool _disposed;
		private StreamRevision? _currentRevision;

		public override ReadResponse Current => _current;
		public string SubscriptionId { get; }

		public StreamSubscription(
			IPublisher bus,
			IExpiryStrategy expiryStrategy,
			string streamName,
			StreamRevision? checkpoint,
			bool resolveLinks,
			ClaimsPrincipal user,
			bool requiresLeader,
			CancellationToken cancellationToken) {
			_expiryStrategy = expiryStrategy;
			_subscriptionId = Guid.NewGuid();
			_bus = Ensure.NotNull(bus);
			_streamName = Ensure.NotNullOrEmpty(streamName);
			_resolveLinks = resolveLinks;
			_user = user;
			_requiresLeader = requiresLeader;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_channel = Channel.CreateBounded<ReadResponse>(BoundedChannelOptions);
			_liveEvents = Channel.CreateBounded<(ulong, ResolvedEvent)>(LiveChannelOptions);
			_currentRevision = null;

			SubscriptionId = _subscriptionId.ToString();

			Subscribe(checkpoint, _cts.Token);
		}

		public override ValueTask DisposeAsync() {
			if (_disposed) {
				return ValueTask.CompletedTask;
			}

			Log.Verbose("Subscription {subscriptionId} to {streamName} disposed.", _subscriptionId, _streamName);
			_disposed = true;

			Unsubscribe();

			_cts.Cancel();
			_cts.Dispose();

			return ValueTask.CompletedTask;
		}

		public override async ValueTask<bool> MoveNextAsync() {
ReadLoop:

			if (!await _channel.Reader.WaitToReadAsync(_cts.Token)) {
				return false;
			}

			var readResponse = await _channel.Reader.ReadAsync(_cts.Token);

			if (readResponse is ReadResponse.EventReceived eventReceived) {
				var @event = eventReceived.Event;
				var streamRevision = StreamRevision.FromInt64(@event.OriginalEventNumber);

				if (_currentRevision.HasValue && streamRevision <= _currentRevision.Value) {
					// this should no longer happen
					Log.Warning(
						"Subscription {subscriptionId} to {streamName} skipping event {streamRevision} as it is less than {currentRevision}.",
						_subscriptionId, _streamName, streamRevision, _currentRevision);

					goto ReadLoop;
				}

				_currentRevision = streamRevision;

				Log.Verbose("Subscription {subscriptionId} to {streamName} seen event {streamRevision}.", _subscriptionId, _streamName, streamRevision);
			}

			_current = readResponse;

			return true;
		}

		private void Subscribe(StreamRevision? checkpoint, CancellationToken ct) {
			Task.Factory.StartNew(() => MainLoop(checkpoint, ct), ct);
		}

		private static long ConvertCheckpoint(StreamRevision? checkpoint, long lastLiveEventNumber) {
			if (!checkpoint.HasValue)
				return -1;

			if (checkpoint == StreamRevision.End)
				return lastLiveEventNumber;

			return checkpoint.Value.ToInt64();
		}

		private async Task MainLoop(StreamRevision? checkpointRevision, CancellationToken ct) {
			try {
				Log.Debug("Subscription {subscriptionId} to {streamName} has started at checkpoint {streamRevision:N0}",
					_subscriptionId, _streamName, checkpointRevision?.ToString() ?? "Start");

				var confirmationLastEventNumber = await SubscribeToLive();
				await ConfirmSubscription(ct);

				// the event number we most recently sent on towards the client.
				// (we should send on events _after_ this)
				var checkpoint = ConvertCheckpoint(checkpointRevision, confirmationLastEventNumber);

				// the most recently read sequence number from the live channel. 0 when we haven't read any.
				var sequenceNumber = 0UL;

				if (checkpoint >= confirmationLastEventNumber)
					(checkpoint, sequenceNumber) = await GoLive(checkpoint, sequenceNumber, ct);

				while (true) {
					ct.ThrowIfCancellationRequested();
					checkpoint = await CatchUp(checkpoint, ct);
					(checkpoint, sequenceNumber) = await GoLive(checkpoint, sequenceNumber, ct);
				}
			} catch (Exception ex) {
				if (ex is not (OperationCanceledException or ReadResponseException.StreamDeleted)) {
					Log.Error(ex, "Subscription {subscriptionId} to {streamName} experienced an error.", _subscriptionId, _streamName);
				}

				_channel.Writer.TryComplete(ex);
			} finally {
				Log.Debug("Subscription {subscriptionId} to {streamName} has ended.", _subscriptionId, _streamName);
			}
		}

		private async Task NotifyCaughtUp(long checkpoint, CancellationToken ct) {
			Log.Debug("Subscription {subscriptionId} to {streamName} caught up at checkpoint {streamRevision:N0}.", _subscriptionId, _streamName, checkpoint);

			await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionCaughtUp(), ct);
		}

		private async Task NotifyFellBehind(long checkpoint, CancellationToken ct) {
			Log.Debug("Subscription {subscriptionId} to {streamName} fell behind at checkpoint {streamRevision:N0}.", _subscriptionId, _streamName, checkpoint);

			await _channel.Writer.WriteAsync(new ReadResponse.SubscriptionFellBehind(), ct);
		}

		private async ValueTask<(long, ulong)> GoLive(long checkpoint, ulong sequenceNumber, CancellationToken ct) {
			await NotifyCaughtUp(checkpoint, ct);

			await foreach (var liveEvent in _liveEvents.Reader.ReadAllAsync(ct)) {
				var sequenceCorrect = liveEvent.SequenceNumber == sequenceNumber + 1;
				sequenceNumber = liveEvent.SequenceNumber;

				if (liveEvent.ResolvedEvent.OriginalEventNumber <= checkpoint) {
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
					// switch back to catch up to make sure we didn't miss anything we wanted to send.
					await NotifyFellBehind(checkpoint, ct);
					return (checkpoint, sequenceNumber);
				}

				// this is the next event to send towards the client. send it and update the checkpoint
				await SendEventToSubscription(liveEvent.ResolvedEvent, ct);
				checkpoint = liveEvent.ResolvedEvent.OriginalEventNumber;
			}

			throw new Exception($"Unexpected error: live events channel for subscription {_subscriptionId} to {_streamName} completed without exception");
		}

		private Task<long> CatchUp(long checkpoint, CancellationToken ct) {
			var startEventNumber = checkpoint + 1;
			Log.Verbose("Subscription {subscriptionId} to {streamName} is catching up from checkpoint {checkpoint:N0}", _subscriptionId, _streamName, checkpoint);

			var catchupCompletionTcs = new TaskCompletionSource<long>();

			// this is a safe use of AsyncTaskEnvelope. Only one call to OnMessage will be running
			// at any given time because we only expect one reply and that reply kicks off the next read.
			AsyncTaskEnvelope envelope = null;
			envelope = new(OnMessage, ct);

			ReadPage(startEventNumber, envelope, ct);

			return catchupCompletionTcs.Task;

			async Task OnMessage(Message message, CancellationToken token) {
				try {
					if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
						throw ex;
					}

					if (message is not ClientMessage.ReadStreamEventsForwardCompleted completed) {
						throw ReadResponseException.UnknownMessage.Create<ClientMessage.ReadStreamEventsForwardCompleted>(message);
					}

					switch (completed.Result) {
						case ReadStreamResult.Success:
							foreach (var @event in completed.Events) {
								var streamRevision = StreamRevision.FromInt64(@event.OriginalEvent.EventNumber);

								Log.Verbose("Subscription {subscriptionId} to {streamName} received catch-up event {streamRevision}.", _subscriptionId, _streamName, streamRevision);

								await SendEventToSubscription(@event, token);
								checkpoint = @event.OriginalEventNumber;
							}

							if (completed.IsEndOfStream) {
								catchupCompletionTcs.TrySetResult(checkpoint);
								return;
							}

							ReadPage(completed.NextEventNumber, envelope, token);
							return;
						case ReadStreamResult.Expired:
							ReadPage(completed.FromEventNumber, envelope, token);
							return;
						case ReadStreamResult.NoStream:
							catchupCompletionTcs.TrySetResult(checkpoint);
							return;
						case ReadStreamResult.StreamDeleted:
							Log.Debug("Subscription {subscriptionId} to {streamName} stream deleted.", _subscriptionId, _streamName);
							throw new ReadResponseException.StreamDeleted(_streamName);
						case ReadStreamResult.AccessDenied:
							throw new ReadResponseException.AccessDenied();
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

			if (@event.OriginalEvent.EventType == SystemEventTypes.StreamDeleted) {
				throw new ReadResponseException.StreamDeleted(_streamName);
			}
		}

		private Task<long> SubscribeToLive() {
			var nextLiveSequenceNumber = 0UL;
			var confirmationEventNumberTcs = new TaskCompletionSource<long>();

			_bus.Publish(new ClientMessage.SubscribeToStream(Guid.NewGuid(), _subscriptionId, new CallbackEnvelope(OnSubscriptionMessage), _subscriptionId, _streamName, _resolveLinks, _user));

			return confirmationEventNumberTcs.Task;

			void OnSubscriptionMessage(Message message) {
				try {
					if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
						throw ex;
					}

					switch (message) {
						case ClientMessage.SubscriptionConfirmation confirmed:
							long caughtUp = confirmed.LastEventNumber ??
											throw ReadResponseException.UnknownError.Create($"Live subscription {_subscriptionId} to {_streamName} failed to retrieve the last event number.");

							Log.Debug(
								"Subscription {subscriptionId} to {streamName} confirmed. LastEventNumber is {streamRevision:N0}.",
								_subscriptionId, _streamName, caughtUp);

							confirmationEventNumberTcs.TrySetResult(caughtUp);
							return;
						case ClientMessage.SubscriptionDropped dropped:
							Log.Debug(
								"Subscription {subscriptionId} to {streamName} dropped by subscription service: {droppedReason}",
								_subscriptionId, _streamName, dropped.Reason);
							switch (dropped.Reason) {
								case SubscriptionDropReason.AccessDenied:
									throw new ReadResponseException.AccessDenied();
								case SubscriptionDropReason.StreamDeleted:
									throw new ReadResponseException.StreamDeleted(_streamName);
								case SubscriptionDropReason.Unsubscribed:
									return;
								case SubscriptionDropReason.NotFound: // applies only to persistent subscriptions
								default:
									throw ReadResponseException.UnknownError.Create(dropped.Reason);
							}
						case ClientMessage.StreamEventAppeared appeared: {
							Log.Verbose(
								"Subscription {subscriptionId} to {streamName} received live event {streamRevision}.",
								_subscriptionId, _streamName, appeared.Event.OriginalEventNumber);

							if (!_liveEvents.Writer.TryWrite((++nextLiveSequenceNumber, appeared.Event))) {
								// this cannot happen because _liveEvents does not have full mode 'wait'.
								throw new Exception($"Unexpected error: could not write to live events channel for subscription {_subscriptionId} to {_streamName}");
							}

							return;
						}
						default:
							throw ReadResponseException.UnknownMessage.Create<ClientMessage.SubscriptionConfirmation>(message);
					}
				} catch (Exception exception) {
					_liveEvents.Writer.TryComplete(exception);
					confirmationEventNumberTcs.TrySetException(exception);
				}
			}
		}

		private ValueTask ConfirmSubscription(CancellationToken ct) {
			return _channel.Writer.WriteAsync(new ReadResponse.SubscriptionConfirmed(SubscriptionId), ct);
		}

		private void ReadPage(long startEventNumber, IEnvelope envelope, CancellationToken ct) {
			var correlationId = Guid.NewGuid();
			Log.Verbose("Subscription {subscriptionId} to {streamName} reading next page starting from {streamRevision}.", _subscriptionId, _streamName, startEventNumber);

			_bus.Publish(new ClientMessage.ReadStreamEventsForward(
				correlationId, correlationId, envelope,
				_streamName, startEventNumber, ReadBatchSize, _resolveLinks, _requiresLeader, null,
				_user,
				replyOnExpired: true,
				expires: _expiryStrategy.GetExpiry(),
				cancellationToken: ct));
		}

		private void Unsubscribe() => _bus.Publish(new ClientMessage.UnsubscribeFromStream(Guid.NewGuid(), _subscriptionId, new NoopEnvelope(), _user));
	}
}
