using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage.ReaderIndex;
using Serilog;

namespace EventStore.Core.Services.Transport.Grpc {
	partial class Enumerators {
		public class AllSubscription : IAsyncEnumerator<ReadResp> {
			private static readonly ILogger Log = Serilog.Log.ForContext<AllSubscription>();

			private readonly IExpiryStrategy _expiryStrategy;
			private readonly Guid _subscriptionId;
			private readonly IPublisher _bus;
			private readonly bool _resolveLinks;
			private readonly ClaimsPrincipal _user;
			private readonly bool _requiresLeader;
			private readonly IReadIndex _readIndex;
			private readonly ReadReq.Types.Options.Types.UUIDOption _uuidOption;
			private readonly CancellationToken _cancellationToken;
			private readonly Channel<ReadResp> _channel;
			private readonly SemaphoreSlim _semaphore;

			private ReadResp _current;
			private bool _disposed;
			private int _subscriptionStarted;
			private Position? _currentPosition;

			public ReadResp Current => _current;
			public string SubscriptionId { get; }

			public AllSubscription(IPublisher bus,
				IExpiryStrategy expiryStrategy,
				Position? startPosition,
				bool resolveLinks,
				ClaimsPrincipal user,
				bool requiresLeader,
				IReadIndex readIndex,
				ReadReq.Types.Options.Types.UUIDOption uuidOption,
				CancellationToken cancellationToken) {
				if (bus == null) {
					throw new ArgumentNullException(nameof(bus));
				}

				if (readIndex == null) {
					throw new ArgumentNullException(nameof(readIndex));
				}

				_expiryStrategy = expiryStrategy;
				_subscriptionId = Guid.NewGuid();
				_bus = bus;
				_resolveLinks = resolveLinks;
				_user = user;
				_requiresLeader = requiresLeader;
				_readIndex = readIndex;
				_uuidOption = uuidOption;
				_cancellationToken = cancellationToken;
				_channel = Channel.CreateBounded<ReadResp>(BoundedChannelOptions);
				_semaphore = new SemaphoreSlim(1, 1);

				_subscriptionStarted = 0;

				SubscriptionId = _subscriptionId.ToString();

				Subscribe(startPosition);
			}

			public ValueTask DisposeAsync() {
				if (_disposed) {
					return new ValueTask(Task.CompletedTask);
				}

				Log.Information("Subscription {subscriptionId} to $all disposed.", _subscriptionId);

				_disposed = true;
				_channel.Writer.TryComplete();
				Unsubscribe();
				return new ValueTask(Task.CompletedTask);
			}

			public async ValueTask<bool> MoveNextAsync() {
ReadLoop:

				if (!await _channel.Reader.WaitToReadAsync(_cancellationToken).ConfigureAwait(false)) {
					return false;
				}

				var readResp = await _channel.Reader.ReadAsync(_cancellationToken).ConfigureAwait(false);

				if (readResp.Event != null) {
					var @event = readResp.Event;

					var position = new Position(@event.OriginalEvent.CommitPosition,
						@event.OriginalEvent.PreparePosition);

					if (_currentPosition.HasValue && position <= _currentPosition.Value) {
						Log.Verbose("Subscription {subscriptionId} to $all skipping event {position}.", _subscriptionId,
							position);
						goto ReadLoop;
					}

					Log.Verbose(
						"Subscription {subscriptionId} to $all seen event {position}.", _subscriptionId, position);

					_currentPosition = position;
				}

				_current = readResp;

				return true;
			}

			private void Subscribe(Position? startPosition) {
				if (startPosition == Position.End) {
					GoLive(Position.End);
				} else if (startPosition == null || startPosition == Position.Start) {
					CatchUp(Position.Start);
				} else {
					var (commitPosition, preparePosition) = startPosition.Value.ToInt64();
					try {
						var indexResult =
							_readIndex.ReadAllEventsForward(new TFPos(commitPosition, preparePosition), 1);
						CatchUp(Position.FromInt64(indexResult.NextPos.CommitPosition,
							indexResult.NextPos.PreparePosition));
					} catch (Exception ex) {
						Log.Error(ex, "Error starting catch-up subscription {subscriptionId} to $all@{position}",
							_subscriptionId, startPosition);
						throw;
					}
				}
			}

			private void CatchUp(Position startPosition) {
				Log.Information(
					"Catch-up subscription {subscriptionId} to $all@{position} running...",
					_subscriptionId, startPosition);

				ReadPage(startPosition, OnMessage);

				async Task OnMessage(Message message, CancellationToken ct) {
					if (message is ClientMessage.NotHandled notHandled &&
						RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
						Fail(ex);
						return;
					}

					if (!(message is ClientMessage.ReadAllEventsForwardCompleted completed)) {
						Fail(
							RpcExceptions.UnknownMessage<ClientMessage.ReadAllEventsForwardCompleted>(message));
						return;
					}

					switch (completed.Result) {
						case ReadAllResult.Success:
							await ConfirmSubscription().ConfigureAwait(false);

							foreach (var @event in completed.Events) {
								var position = Position.FromInt64(
									@event.OriginalPosition.Value.CommitPosition,
									@event.OriginalPosition.Value.PreparePosition);

								Log.Verbose(
									"Catch-up subscription {subscriptionId} to $all received event {position}.",
									_subscriptionId, position);

								await _channel.Writer.WriteAsync(new ReadResp {
									Event = ConvertToReadEvent(_uuidOption, @event)
								}, ct).ConfigureAwait(false);
							}

							var nextPosition = Position.FromInt64(completed.NextPos.CommitPosition,
								completed.NextPos.PreparePosition);

							if (completed.IsEndOfStream) {
								GoLive(nextPosition);
								return;
							}

							ReadPage(nextPosition, OnMessage);
							return;
						case ReadAllResult.Expired:
							ReadPage(
								Position.FromInt64(
									completed.CurrentPos.CommitPosition,
									completed.CurrentPos.PreparePosition),
								OnMessage);
							return;
						case ReadAllResult.AccessDenied:
							Fail(RpcExceptions.AccessDenied());
							return;
						default:
							Fail(RpcExceptions.UnknownError(completed.Result));
							return;
					}
				}
			}

			private void GoLive(Position startPosition) {
				var liveEvents = Channel.CreateBounded<ResolvedEvent>(BoundedChannelOptions);
				var caughtUpSource = new TaskCompletionSource<Position>();
				var liveMessagesCancelled = 0;

				Log.Information(
					"Live subscription {subscriptionId} to $all running from {position}...",
					_subscriptionId, startPosition);

				_bus.Publish(new ClientMessage.SubscribeToStream(Guid.NewGuid(), _subscriptionId,
					new ContinuationEnvelope(OnSubscriptionMessage, _semaphore, _cancellationToken), _subscriptionId,
					string.Empty, _resolveLinks, _user));

				Task.Factory.StartNew(PumpLiveMessages, _cancellationToken);

				async Task PumpLiveMessages() {
					await caughtUpSource.Task.ConfigureAwait(false);

					await _channel.Writer.WriteAsync(new ReadResp {
						CaughtUp = new ReadResp.Types.CaughtUp()
					}, _cancellationToken).ConfigureAwait(false);

					await foreach (var @event in liveEvents.Reader.ReadAllAsync(_cancellationToken)
						.ConfigureAwait(false)) {
						await _channel.Writer.WriteAsync(new ReadResp {
							Event = ConvertToReadEvent(_uuidOption, @event)
						}, _cancellationToken).ConfigureAwait(false);
					}
				}

				async Task OnSubscriptionMessage(Message message, CancellationToken cancellationToken) {
					if (message is ClientMessage.NotHandled notHandled &&
						RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
						Fail(ex);
						return;
					}

					switch (message) {
						case ClientMessage.SubscriptionConfirmation confirmed:
							await ConfirmSubscription().ConfigureAwait(false);

							var caughtUp = new TFPos(confirmed.LastIndexedPosition,
								confirmed.LastIndexedPosition);
							Log.Verbose(
								"Live subscription {subscriptionId} to $all confirmed at {position}.",
								_subscriptionId, caughtUp);

							if (startPosition != Position.End) {
								ReadHistoricalEvents(startPosition);
							} else {
								NotifyCaughtUp(startPosition);
							}

							void NotifyCaughtUp(Position position) {
								Log.Verbose(
									"Live subscription {subscriptionId} to $all caught up at {position} because the end of stream was reached.",
									_subscriptionId, position);
								caughtUpSource.TrySetResult(position);
							}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
							async Task OnHistoricalEventsMessage(Message message, CancellationToken ct) {
#pragma warning restore CS1998
								if (message is ClientMessage.NotHandled notHandled &&
									RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
									Fail(ex);
									return;
								}

								if (!(message is ClientMessage.ReadAllEventsForwardCompleted completed)) {
									Fail(
										RpcExceptions.UnknownMessage<ClientMessage.ReadAllEventsForwardCompleted>(
											message));
									return;
								}

								switch (completed.Result) {
									case ReadAllResult.Success:
										if (completed.Events.Length == 0 && completed.IsEndOfStream) {
											NotifyCaughtUp(Position.FromInt64(completed.CurrentPos.CommitPosition,
												completed.CurrentPos.PreparePosition));
											return;
										}

										foreach (var @event in completed.Events) {
											var position = @event.OriginalPosition.Value;

											if (position > caughtUp) {
												NotifyCaughtUp(Position.FromInt64(position.CommitPosition,
													position.PreparePosition));
												return;
											}

											if (!_channel.Writer.TryWrite(new ReadResp {
												Event = ConvertToReadEvent(_uuidOption, @event)
											})) {

												ConsumerTooSlow(@event);
												return;
											}
										}

										ReadHistoricalEvents(Position.FromInt64(
											completed.NextPos.CommitPosition,
											completed.NextPos.PreparePosition));
										return;

									case ReadAllResult.Expired:
										ReadHistoricalEvents(Position.FromInt64(
											completed.CurrentPos.CommitPosition,
											completed.CurrentPos.PreparePosition));
										return;

									case ReadAllResult.AccessDenied:
										Fail(RpcExceptions.AccessDenied());
										return;
									default:
										Fail(RpcExceptions.UnknownError(completed.Result));
										return;
								}
							}

							void ReadHistoricalEvents(Position fromPosition) {
								if (fromPosition == Position.End) {
									throw new ArgumentOutOfRangeException(nameof(fromPosition));
								}

								Log.Verbose(
									"Live subscription {subscriptionId} to $all loading any missed events starting from {position}.",
									_subscriptionId, fromPosition);

								ReadPage(fromPosition, OnHistoricalEventsMessage);
							}

							return;
						case ClientMessage.SubscriptionDropped dropped:
							switch (dropped.Reason) {
								case SubscriptionDropReason.AccessDenied:
									Fail(RpcExceptions.AccessDenied());
									return;
								case SubscriptionDropReason.Unsubscribed:
									return;
								default:
									Fail(RpcExceptions.UnknownError(dropped.Reason));
									return;
							}
						case ClientMessage.StreamEventAppeared appeared: {
							if (liveMessagesCancelled == 1) {
								return;
							}

							Log.Verbose(
								"Live subscription {subscriptionId} to $all enqueuing live message {position}.",
								_subscriptionId, appeared.Event.OriginalPosition);

							if (!liveEvents.Writer.TryWrite(appeared.Event)) {
								ConsumerTooSlow(appeared.Event);
							}

							return;
						}
						default:
							Fail(
								RpcExceptions.UnknownMessage<ClientMessage.SubscriptionConfirmation>(message));
							return;
					}
				}

				void ConsumerTooSlow(ResolvedEvent evt) {
					if (Interlocked.Exchange(ref liveMessagesCancelled, 1) != 0)
						return;

					var state = caughtUpSource.Task.Status == TaskStatus.RanToCompletion ? "live" : "transitioning to live";
					var msg = $"Consumer too slow to handle event while {state}. Client resubscription required.";
					Log.Information(
						"Live subscription {subscriptionId} to $all timed out at {position}. {msg}. unsubscribing...",
						_subscriptionId, evt.OriginalPosition.GetValueOrDefault(), msg);

					Unsubscribe();

					liveEvents.Writer.Complete();

					Fail(RpcExceptions.Timeout(msg));
				}

				void Fail(Exception exception) {
					this.Fail(exception);
					caughtUpSource.TrySetException(exception);
				}
			}

			private ValueTask ConfirmSubscription() => Interlocked.CompareExchange(ref _subscriptionStarted, 1, 0) != 0
				? new ValueTask(Task.CompletedTask)
				: _channel.Writer.WriteAsync(new ReadResp {
					Confirmation = new ReadResp.Types.SubscriptionConfirmation {
						SubscriptionId = SubscriptionId
					}
				}, _cancellationToken);

			private void Fail(Exception exception) {
				Interlocked.Exchange(ref _subscriptionStarted, 1);
				_channel.Writer.TryComplete(exception);
			}

			private void ReadPage(Position position, Func<Message, CancellationToken, Task> onMessage) {
				Guid correlationId = Guid.NewGuid();
				Log.Verbose(
					"Subscription {subscriptionId} to $all reading next page starting from {nextRevision}.",
					_subscriptionId, position);

				var (commitPosition, preparePosition) = position.ToInt64();

				_bus.Publish(new ClientMessage.ReadAllEventsForward(
					correlationId, correlationId, new ContinuationEnvelope(onMessage, _semaphore, _cancellationToken),
					commitPosition, preparePosition, ReadBatchSize, _resolveLinks, _requiresLeader, null, _user,
					replyOnExpired: true,
					expires: _expiryStrategy.GetExpiry(),
					cancellationToken: _cancellationToken));
			}

			private void Unsubscribe() => _bus.Publish(new ClientMessage.UnsubscribeFromStream(Guid.NewGuid(),
				_subscriptionId, new NoopEnvelope(), _user));
		}
	}
}
