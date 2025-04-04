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
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;

namespace EventStore.Core.Services.Transport.Grpc {
	static partial class Enumerators {
		public abstract class StreamSubscription : IAsyncEnumerator<ReadResp> {
			protected static readonly ILogger Log = Serilog.Log.ForContext<StreamSubscription>();
			public abstract ValueTask DisposeAsync();
			public abstract ValueTask<bool> MoveNextAsync();
			public abstract ReadResp Current { get; }
		}

		public class StreamSubscription<TStreamId> : StreamSubscription {
			private readonly IExpiryStrategy _expiryStrategy;
			private readonly Guid _subscriptionId;
			private readonly IPublisher _bus;
			private readonly string _streamName;
			private readonly bool _resolveLinks;
			private readonly ClaimsPrincipal _user;
			private readonly bool _requiresLeader;
			private readonly ReadReq.Types.Options.Types.UUIDOption _uuidOption;
			private readonly CancellationToken _cancellationToken;
			private readonly Channel<ReadResp> _channel;
			private readonly SemaphoreSlim _semaphore;

			private int _subscriptionStarted;
			private ReadResp _current;
			private bool _disposed;
			private StreamRevision? _currentRevision;

			public override ReadResp Current => _current;
			public string SubscriptionId { get; }

			public StreamSubscription(
				IPublisher bus,
				IExpiryStrategy expiryStrategy,
				string streamName,
				StreamRevision? startRevision,
				bool resolveLinks,
				ClaimsPrincipal user,
				bool requiresLeader,
				ReadReq.Types.Options.Types.UUIDOption uuidOption,
				CancellationToken cancellationToken) {
				if (bus == null) {
					throw new ArgumentNullException(nameof(bus));
				}

				if (streamName == null) {
					throw new ArgumentNullException(nameof(streamName));
				}

				_expiryStrategy = expiryStrategy;
				_subscriptionId = Guid.NewGuid();
				_bus = bus;
				_streamName = streamName;
				_resolveLinks = resolveLinks;
				_user = user;
				_requiresLeader = requiresLeader;
				_uuidOption = uuidOption;
				_cancellationToken = cancellationToken;
				_subscriptionStarted = 0;
				_channel = Channel.CreateBounded<ReadResp>(BoundedChannelOptions);
				_semaphore = new SemaphoreSlim(1, 1);
				_currentRevision = null;

				SubscriptionId = _subscriptionId.ToString();

				Subscribe(startRevision);
			}

			public override ValueTask DisposeAsync() {
				if (_disposed) {
					return new ValueTask(Task.CompletedTask);
				}

				Log.Information("Live subscription {subscriptionId} to {streamName} disposed.", _subscriptionId,
					_streamName);
				_disposed = true;
				_channel.Writer.TryComplete();
				Unsubscribe();
				return new ValueTask(Task.CompletedTask);
			}

			public override async ValueTask<bool> MoveNextAsync() {
ReadLoop:

				if (!await _channel.Reader.WaitToReadAsync(_cancellationToken).ConfigureAwait(false)) {
					return false;
				}

				var readResp = await _channel.Reader.ReadAsync(_cancellationToken).ConfigureAwait(false);

				if (readResp.Event != null) {
					var @event = readResp.Event;

					if (@event.OriginalEvent.Metadata[Constants.Metadata.Type] == SystemEventTypes.StreamDeleted) {
						Fail(RpcExceptions.StreamDeleted(_streamName));
					}

					var streamRevision = new StreamRevision(@event.OriginalEvent.StreamRevision);
					if (_currentRevision.HasValue && streamRevision <= _currentRevision.Value) {
						Log.Verbose(
							"Subscription {subscriptionId} to {streamName} skipping event {streamRevision} as it is less than {currentRevision}.",
							_subscriptionId, _streamName, streamRevision, _currentRevision);

						goto ReadLoop;
					}

					_currentRevision = streamRevision;

					Log.Verbose(
						"Subscription {subscriptionId} to {streamName} seen event {streamRevision}.",
						_subscriptionId, _streamName, streamRevision);
				}

				_current = readResp;

				return true;
			}

			private void Subscribe(StreamRevision? startRevision) {
				if (startRevision == StreamRevision.End) {
					GoLive(StreamRevision.End);
				} else {
					_currentRevision = startRevision;
					CatchUp(startRevision + 1 ?? StreamRevision.Start);
				}
			}

			private void CatchUp(StreamRevision? startRevision) {
				Log.Information(
					"Catch-up subscription {subscriptionId} to {streamName}@{streamRevision} running...",
					_subscriptionId, _streamName, startRevision);

				ReadPage(startRevision ?? StreamRevision.Start, OnMessage);

				async Task OnMessage(Message message, CancellationToken ct) {
					if (message is ClientMessage.NotHandled notHandled &&
						RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
						Fail(ex);
						return;
					}

					if (!(message is ClientMessage.ReadStreamEventsForwardCompleted completed)) {
						Fail(
							RpcExceptions.UnknownMessage<ClientMessage.ReadStreamEventsForwardCompleted>(message));
						return;
					}

					switch (completed.Result) {
						case ReadStreamResult.Success:
							await ConfirmSubscription().ConfigureAwait(false);
							foreach (var @event in completed.Events) {
								var streamRevision = StreamRevision.FromInt64(@event.OriginalEvent.EventNumber);

								Log.Verbose(
									"Catch-up subscription {subscriptionId} to {streamName} received event {streamRevision}.",
									_subscriptionId, _streamName, streamRevision);

								await _channel.Writer.WriteAsync(new ReadResp {
									Event = ConvertToReadEvent(_uuidOption, @event)
								}, ct).ConfigureAwait(false);
							}

							if (completed.IsEndOfStream) {
								GoLive(StreamRevision.FromInt64(completed.NextEventNumber));
								return;
							}

							ReadPage(StreamRevision.FromInt64(completed.NextEventNumber), OnMessage);
							return;
						case ReadStreamResult.Expired:
							ReadPage(StreamRevision.FromInt64(completed.FromEventNumber), OnMessage);
							return;
						case ReadStreamResult.NoStream:
							await ConfirmSubscription().ConfigureAwait(false);
							await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
							ReadPage(startRevision ?? StreamRevision.Start, OnMessage);
							return;
						case ReadStreamResult.StreamDeleted:
							Log.Verbose(
								"Live subscription {subscriptionId} to {streamName} stream deleted.",
								_subscriptionId, _streamName);
							Fail(RpcExceptions.StreamDeleted(_streamName));
							return;
						case ReadStreamResult.AccessDenied:
							Fail(RpcExceptions.AccessDenied());
							return;
						default:
							Fail(RpcExceptions.UnknownError(completed.Result));
							return;
					}
				}
			}

			private void GoLive(StreamRevision startRevision) {
				var liveEvents = Channel.CreateBounded<ResolvedEvent>(BoundedChannelOptions);
				var caughtUpSource = new TaskCompletionSource();
				var liveMessagesCancelled = 0;

				Log.Information(
					"Live subscription {subscriptionId} to {streamName} running from {streamRevision}...",
					_subscriptionId, _streamName, startRevision);

				_bus.Publish(new ClientMessage.SubscribeToStream(Guid.NewGuid(), _subscriptionId,
					new ContinuationEnvelope(OnSubscriptionMessage, _semaphore, _cancellationToken), _subscriptionId,
					_streamName, _resolveLinks, _user));

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

				async Task OnSubscriptionMessage(Message message, CancellationToken ct) {
					if (message is ClientMessage.NotHandled notHandled &&
						RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
						Fail(ex);
						return;
					}

					switch (message) {
						case ClientMessage.SubscriptionConfirmation confirmed:
							await ConfirmSubscription().ConfigureAwait(false);

							var caughtUp = StreamRevision.FromInt64(confirmed.LastEventNumber.Value);
							Log.Verbose(
								"Live subscription {subscriptionId} to {streamName} confirmed at {streamRevision}.",
								_subscriptionId, _streamName, caughtUp);

							if (startRevision != StreamRevision.End) {
								ReadHistoricalEvents(startRevision);
							} else {
								NotifyCaughtUp(startRevision);
							}

							void NotifyCaughtUp(StreamRevision streamRevision) {
								Log.Verbose(
									"Live subscription {subscriptionId} to {streamName} caught up at {streamRevision} because the end of stream was reached.",
									_subscriptionId, _streamName, streamRevision);
								caughtUpSource.TrySetResult();
							}

							async Task OnHistoricalEventsMessage(Message message, CancellationToken ct) {
								if (message is ClientMessage.NotHandled notHandled &&
									RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
									Fail(ex);
									return;
								}

								if (!(message is ClientMessage.ReadStreamEventsForwardCompleted completed)) {
									Fail(
										RpcExceptions.UnknownMessage<ClientMessage.ReadStreamEventsForwardCompleted>(
											message));
									return;
								}

								switch (completed.Result) {
									case ReadStreamResult.Success:
										if (completed.Events.Length == 0 && completed.IsEndOfStream) {
											NotifyCaughtUp(StreamRevision.FromInt64(completed.FromEventNumber));
											return;
										}

										foreach (var @event in completed.Events) {
											var streamRevision = StreamRevision.FromInt64(@event.OriginalEventNumber);

											if (streamRevision > caughtUp) {
												NotifyCaughtUp(streamRevision);
												return;
											}

											Log.Verbose(
												"Live subscription {subscriptionId} to {streamName} enqueuing historical message {streamRevision}.",
												_subscriptionId, _streamName, streamRevision);
											if (!_channel.Writer.TryWrite(new ReadResp {
												Event = ConvertToReadEvent(_uuidOption, @event)
											})) {

												ConsumerTooSlow(@event);
												return;
											}
										}

										ReadHistoricalEvents(StreamRevision.FromInt64(completed.NextEventNumber));

										return;
									case ReadStreamResult.Expired:
										ReadHistoricalEvents(StreamRevision.FromInt64(completed.FromEventNumber));
										return;
									case ReadStreamResult.NoStream:
										Log.Verbose(
											"Live subscription {subscriptionId} to {streamName} stream not found.",
											_subscriptionId, _streamName);
										await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
										ReadHistoricalEvents(startRevision);
										return;
									case ReadStreamResult.StreamDeleted:
										Log.Verbose(
											"Live subscription {subscriptionId} to {streamName} stream deleted.",
											_subscriptionId, _streamName);
										Fail(RpcExceptions.StreamDeleted(_streamName));
										return;
									case ReadStreamResult.AccessDenied:
										Fail(RpcExceptions.AccessDenied());
										return;
									default:
										Fail(RpcExceptions.UnknownError(completed.Result));
										return;
								}
							}

							void ReadHistoricalEvents(StreamRevision fromStreamRevision) {
								if (fromStreamRevision == StreamRevision.End) {
									throw new ArgumentOutOfRangeException(nameof(fromStreamRevision));
								}

								Log.Verbose(
									"Live subscription {subscriptionId} to {streamName} loading any missed events starting from {streamRevision}.",
									_subscriptionId, _streamName, fromStreamRevision);

								ReadPage(fromStreamRevision, OnHistoricalEventsMessage);
							}

							return;
						case ClientMessage.SubscriptionDropped dropped:
							Log.Debug(
								"Live subscription {subscriptionId} to {streamName} dropped: {droppedReason}",
								_subscriptionId, _streamName, dropped.Reason);
							switch (dropped.Reason) {
								case SubscriptionDropReason.AccessDenied:
									Fail(RpcExceptions.AccessDenied());
									return;
								case SubscriptionDropReason.NotFound:
									await _channel.Writer.WriteAsync(new ReadResp {
										StreamNotFound = new ReadResp.Types.StreamNotFound {
											StreamIdentifier = _streamName
										}
									}, _cancellationToken).ConfigureAwait(false);
									_channel.Writer.Complete();
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
								"Live subscription {subscriptionId} to {streamName} received event {streamRevision}.",
								_subscriptionId, _streamName, appeared.Event.OriginalEventNumber);

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
						"Live subscription {subscriptionId} to {streamName} timed out at {streamRevision}. {msg}. unsubscribing...",
						_subscriptionId, _streamName,
						StreamRevision.FromInt64(evt.OriginalEventNumber), msg);

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

			private void ReadPage(StreamRevision startRevision, Func<Message, CancellationToken, Task> onMessage) {
				Guid correlationId = Guid.NewGuid();
				Log.Verbose(
					"Subscription {subscriptionId} to {streamName} reading next page starting from {nextRevision}.",
					_subscriptionId, _streamName, startRevision);

				_bus.Publish(new ClientMessage.ReadStreamEventsForward(
					correlationId, correlationId, new ContinuationEnvelope(onMessage, _semaphore, _cancellationToken),
					_streamName, startRevision.ToInt64(), ReadBatchSize, _resolveLinks, _requiresLeader, null,
					_user,
					replyOnExpired: true,
					expires: _expiryStrategy.GetExpiry(),
					cancellationToken: _cancellationToken));
			}

			private void Unsubscribe() => _bus.Publish(new ClientMessage.UnsubscribeFromStream(Guid.NewGuid(),
				_subscriptionId, new NoopEnvelope(), _user));
		}
	}
}
