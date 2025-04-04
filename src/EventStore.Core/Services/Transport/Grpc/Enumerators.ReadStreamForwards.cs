using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;

namespace EventStore.Core.Services.Transport.Grpc {
	partial class Enumerators {
		public class ReadStreamForwards : IAsyncEnumerator<ReadResp> {
			private readonly IPublisher _bus;
			private readonly string _streamName;
			private readonly ulong _maxCount;
			private readonly bool _resolveLinks;
			private readonly ClaimsPrincipal _user;
			private readonly bool _requiresLeader;
			private readonly DateTime _deadline;
			private readonly ReadReq.Types.Options.Types.UUIDOption _uuidOption;
			private readonly uint _compatibility;
			private readonly CancellationToken _cancellationToken;
			private readonly SemaphoreSlim _semaphore;
			private readonly Channel<ReadResp> _channel;

			private ReadResp _current;

			public ReadResp Current => _current;

			public ReadStreamForwards(IPublisher bus,
				string streamName,
				StreamRevision startRevision,
				ulong maxCount,
				bool resolveLinks,
				ClaimsPrincipal user,
				bool requiresLeader,
				DateTime deadline,
				ReadReq.Types.Options.Types.UUIDOption uuidOption,
				uint compatibility,
				CancellationToken cancellationToken) {
				_bus = bus ?? throw new ArgumentNullException(nameof(bus));
				_streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
				_maxCount = maxCount;
				_resolveLinks = resolveLinks;
				_user = user;
				_requiresLeader = requiresLeader;
				_deadline = deadline;
				_uuidOption = uuidOption;
				_compatibility = compatibility;
				_cancellationToken = cancellationToken;
				_semaphore = new SemaphoreSlim(1, 1);
				_channel = Channel.CreateBounded<ReadResp>(BoundedChannelOptions);

				ReadPage(startRevision);
			}

			public ValueTask DisposeAsync() {
				_channel.Writer.TryComplete();
				return new ValueTask(Task.CompletedTask);
			}

			public async ValueTask<bool> MoveNextAsync() {
				if (!await _channel.Reader.WaitToReadAsync(_cancellationToken).ConfigureAwait(false)) {
					return false;
				}

				_current = await _channel.Reader.ReadAsync(_cancellationToken).ConfigureAwait(false);

				return true;
			}

			private void ReadPage(StreamRevision startRevision, ulong readCount = 0) {
				Guid correlationId = Guid.NewGuid();

				_bus.Publish(new ClientMessage.ReadStreamEventsForward(
					correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, _cancellationToken),
					_streamName, startRevision.ToInt64(), (int)Math.Min(ReadBatchSize, _maxCount), _resolveLinks,
					_requiresLeader, null, _user, replyOnExpired: false, expires: _deadline,
					cancellationToken: _cancellationToken));

				async Task OnMessage(Message message, CancellationToken ct) {
					if (message is ClientMessage.NotHandled notHandled &&
						RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
						_channel.Writer.TryComplete(ex);
						return;
					}

					if (!(message is ClientMessage.ReadStreamEventsForwardCompleted completed)) {
						_channel.Writer.TryComplete(
							RpcExceptions.UnknownMessage<ClientMessage.ReadStreamEventsForwardCompleted>(message));
						return;
					}

					switch (completed.Result) {
						case ReadStreamResult.Success:
							if (readCount == 0 && _compatibility >= 1) {
								if (completed.Events.Length == 0) {
									var firstStreamPosition = StreamRevision.FromInt64(completed.NextEventNumber);
									if (startRevision != firstStreamPosition) {
										await _channel.Writer.WriteAsync(new() {
											FirstStreamPosition = firstStreamPosition
										}, ct).ConfigureAwait(false);
									}
								}
							}

							foreach (var @event in completed.Events) {
								if (readCount >= _maxCount) {
									break;
								}
								await _channel.Writer.WriteAsync(new() {
									Event = ConvertToReadEvent(_uuidOption, @event)
								}, ct).ConfigureAwait(false);
								readCount++;
							}

							if (!completed.IsEndOfStream && readCount < _maxCount) {
								ReadPage(StreamRevision.FromInt64(completed.NextEventNumber), readCount);
								return;
							}

							if (_compatibility >= 1) {
								await _channel.Writer.WriteAsync(new() {
									LastStreamPosition = StreamRevision.FromInt64(completed.LastEventNumber)
								}, ct).ConfigureAwait(false);
							}

							_channel.Writer.TryComplete();
							return;

						case ReadStreamResult.NoStream:
							await _channel.Writer.WriteAsync(new() {
								StreamNotFound = new() {
									StreamIdentifier = _streamName
								}
							}, _cancellationToken).ConfigureAwait(false);
							_channel.Writer.TryComplete();
							return;
						case ReadStreamResult.StreamDeleted:
							_channel.Writer.TryComplete(RpcExceptions.StreamDeleted(_streamName));
							return;
						case ReadStreamResult.AccessDenied:
							_channel.Writer.TryComplete(RpcExceptions.AccessDenied());
							return;
						default:
							_channel.Writer.TryComplete(RpcExceptions.UnknownError(completed.Result));
							return;
					}
				}
			}
		}
	}
}
