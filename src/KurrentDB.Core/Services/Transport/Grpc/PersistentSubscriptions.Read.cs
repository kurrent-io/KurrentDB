// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Plugins.Authorization;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Grpc;
using Serilog;
using static EventStore.Plugins.Authorization.Operations.Subscriptions;
using static KurrentDB.Core.Messages.ClientMessage.PersistentSubscriptionNackEvents;
using Empty = EventStore.Client.Empty;
using UUID = EventStore.Client.UUID;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation ProcessMessagesOperation = new(ProcessMessages);

	public override async Task Read(IAsyncStreamReader<ReadReq> requestStream,
		IServerStreamWriter<ReadResp> responseStream, ServerCallContext context) {
		if (!await requestStream.MoveNext()) {
			return;
		}

		if (requestStream.Current.ContentCase != ReadReq.ContentOneofCase.Options) {
			throw new InvalidOperationException();
		}

		var options = requestStream.Current.Options;
		var user = context.GetHttpContext().User;

		string streamId = options.StreamOptionCase switch {
			ReadReq.Types.Options.StreamOptionOneofCase.StreamIdentifier => options.StreamIdentifier,
			ReadReq.Types.Options.StreamOptionOneofCase.All => SystemStreams.AllStream,
			_ => throw new InvalidOperationException()
		};

		if (!await _authorizationProvider.CheckAccessAsync(user, ProcessMessagesOperation.WithParameter(Parameters.StreamId(streamId)), context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var connectionName = context.RequestHeaders.FirstOrDefault(x => x.Key == Constants.Headers.ConnectionName)?.Value ?? "<unknown>";
		var correlationId = Guid.NewGuid();
		var uuidOptionsCase = options.UuidOption.ContentCase;

		var enumerator = new PersistentStreamSubscriptionEnumerator(correlationId, connectionName,
			_publisher, streamId, options.GroupName, options.BufferSize, user, context.CancellationToken);
		await using var _ = enumerator;

		var subscriptionId = await enumerator.Started;

		var read = ValueTask.CompletedTask;
		var cts = new CancellationTokenSource();
		try {
			read = PumpRequestStream(cts.Token);

			await responseStream.WriteAsync(new ReadResp {
				SubscriptionConfirmation = new ReadResp.Types.SubscriptionConfirmation {
					SubscriptionId = subscriptionId
				}
			});

			while (await enumerator.MoveNextAsync()) {
				await responseStream.WriteAsync(new ReadResp {
					Event = ConvertToReadEvent(enumerator.Current)
				});
			}
		} catch (IOException) {
			Log.Information("Subscription {correlationId} to {subscriptionId} disposed. The request stream was closed.", correlationId, subscriptionId);
		} finally {
			// make sure we stop reading the request stream before leaving this method
			cts.Cancel();
			await read;
		}

		async ValueTask PumpRequestStream(CancellationToken token) {
			try {
				await requestStream.ForEachAsync(HandleAckNack, token);
			} catch {
				// ignore
			}
		}

		ValueTask HandleAckNack(ReadReq request) {
			_publisher.Publish(request.ContentCase switch {
				ReadReq.ContentOneofCase.Ack => new ClientMessage.PersistentSubscriptionAckEvents(
					correlationId, correlationId, new NoopEnvelope(), subscriptionId,
					request.Ack.Ids.Select(id => Uuid.FromDto(id).ToGuid()).ToArray(), user),
				ReadReq.ContentOneofCase.Nack =>
					new ClientMessage.PersistentSubscriptionNackEvents(
						correlationId, correlationId, new NoopEnvelope(), subscriptionId,
						request.Nack.Reason, request.Nack.Action switch {
							ReadReq.Types.Nack.Types.Action.Unknown => NakAction.Unknown,
							ReadReq.Types.Nack.Types.Action.Park => NakAction.Park,
							ReadReq.Types.Nack.Types.Action.Retry => NakAction.Retry,
							ReadReq.Types.Nack.Types.Action.Skip => NakAction.Skip,
							ReadReq.Types.Nack.Types.Action.Stop => NakAction.Stop,
							_ => throw RpcExceptions.InvalidArgument(request.Nack.Action)
						},
						request.Nack.Ids.Select(id => Uuid.FromDto(id).ToGuid()).ToArray(), user),
				_ => throw RpcExceptions.InvalidArgument(request.ContentCase)
			});

			return new ValueTask(Task.CompletedTask);
		}

		ReadResp.Types.ReadEvent.Types.RecordedEvent ConvertToRecordedEvent(EventRecord e, long? commitPosition, long? preparePosition) {
			if (e == null)
				return null;
			var position = Position.FromInt64(commitPosition ?? -1, preparePosition ?? -1);
			var @event = new ReadResp.Types.ReadEvent.Types.RecordedEvent {
				Id = uuidOptionsCase switch {
					ReadReq.Types.Options.Types.UUIDOption.ContentOneofCase.String => new UUID {
						String = e.EventId.ToString()
					},
					_ => Uuid.FromGuid(e.EventId).ToDto()
				},
				StreamIdentifier = e.EventStreamId,
				StreamRevision = StreamRevision.FromInt64(e.EventNumber),
				CommitPosition = position.CommitPosition,
				PreparePosition = position.PreparePosition,
				Data = ByteString.CopyFrom(e.Data.Span),
				CustomMetadata = ByteString.CopyFrom(e.Metadata.Span)
			};

			@event.Metadata.Add(e.GetMetadataDictionary());

			return @event;
		}

		ReadResp.Types.ReadEvent ConvertToReadEvent((ResolvedEvent, int) _) {
			var (e, retryCount) = _;
			var readEvent = new ReadResp.Types.ReadEvent {
				Link = ConvertToRecordedEvent(e.Link, e.OriginalPosition?.CommitPosition, e.OriginalPosition?.PreparePosition),
				Event = ConvertToRecordedEvent(e.Event, e.OriginalPosition?.CommitPosition, e.OriginalPosition?.PreparePosition),
				RetryCount = retryCount
			};
			if (e.OriginalPosition.HasValue) {
				var position = Position.FromInt64(e.OriginalPosition.Value.CommitPosition, e.OriginalPosition.Value.PreparePosition);
				readEvent.CommitPosition = position.CommitPosition;
			} else {
				readEvent.NoPosition = new Empty();
			}

			return readEvent;
		}
	}

	private class PersistentStreamSubscriptionEnumerator : IAsyncEnumerator<(ResolvedEvent resolvedEvent, int retryCount)> {
		private readonly TaskCompletionSource<string> _subscriptionIdSource;
		private readonly IPublisher _publisher;
		private readonly Guid _correlationId;
		private readonly ClaimsPrincipal _user;
		private readonly CancellationToken _cancellationToken;
		private readonly Channel<(ResolvedEvent, int)> _channel;

		private (ResolvedEvent, int) _current;

		public (ResolvedEvent resolvedEvent, int retryCount) Current => _current;
		public Task<string> Started => _subscriptionIdSource.Task;

		public PersistentStreamSubscriptionEnumerator(Guid correlationId,
			string connectionName,
			IPublisher publisher,
			string streamName,
			string groupName,
			int bufferSize,
			ClaimsPrincipal user,
			CancellationToken cancellationToken) {
			ArgumentNullException.ThrowIfNull(connectionName);
			ArgumentNullException.ThrowIfNull(streamName);
			ArgumentNullException.ThrowIfNull(groupName);

			_correlationId = Ensure.NotEmptyGuid(correlationId);
			_publisher = Ensure.NotNull(publisher);
			_subscriptionIdSource = new();
			_user = user;
			_cancellationToken = cancellationToken;
			_channel = Channel.CreateBounded<(ResolvedEvent, int)>(new BoundedChannelOptions(bufferSize) {
				SingleReader = true,
				SingleWriter = false,
				FullMode = BoundedChannelFullMode.DropWrite
			});

			var semaphore = new SemaphoreSlim(1, 1);

			switch (streamName) {
				case SystemStreams.AllStream:
					publisher.Publish(new ClientMessage.ConnectToPersistentSubscriptionToAll(correlationId,
						correlationId,
						new ContinuationEnvelope(OnMessage, semaphore, _cancellationToken), correlationId,
						connectionName,
						groupName, bufferSize, string.Empty, user));
					break;
				default:
					publisher.Publish(new ClientMessage.ConnectToPersistentSubscriptionToStream(correlationId,
						correlationId,
						new ContinuationEnvelope(OnMessage, semaphore, _cancellationToken), correlationId,
						connectionName,
						groupName, streamName, bufferSize, string.Empty, user));
					break;
			}

			async Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
					_subscriptionIdSource.TrySetException(ex);
					return;
				}

				switch (message) {
					case ClientMessage.SubscriptionDropped dropped:
						switch (dropped.Reason) {
							case SubscriptionDropReason.AccessDenied:
								Fail(RpcExceptions.AccessDenied());
								return;
							case SubscriptionDropReason.NotFound:
							case SubscriptionDropReason.PersistentSubscriptionDeleted:
								Fail(RpcExceptions.PersistentSubscriptionDoesNotExist(streamName, groupName));
								return;
							case SubscriptionDropReason.SubscriberMaxCountReached:
								Fail(RpcExceptions.PersistentSubscriptionMaximumSubscribersReached(streamName, groupName));
								return;
							case SubscriptionDropReason.Unsubscribed:
								Fail(RpcExceptions.PersistentSubscriptionDropped(streamName, groupName));
								return;
							default:
								Fail(RpcExceptions.UnknownError(dropped.Reason));
								return;
						}
					case ClientMessage.PersistentSubscriptionConfirmation confirmation:
						_subscriptionIdSource.TrySetResult(confirmation.SubscriptionId);
						return;
					case ClientMessage.PersistentSubscriptionStreamEventAppeared appeared:
						await _channel.Writer.WriteAsync((appeared.Event, appeared.RetryCount), ct);
						return;
					default:
						Fail(RpcExceptions
							.UnknownMessage<ClientMessage.PersistentSubscriptionConfirmation>(message));
						return;
				}
			}

			void Fail(Exception ex) {
				_channel.Writer.TryComplete(ex);
				_subscriptionIdSource.TrySetException(ex);
			}
		}

		public ValueTask DisposeAsync() {
			_publisher.Publish(new ClientMessage.UnsubscribeFromStream(Guid.NewGuid(), _correlationId, new NoopEnvelope(), _user));
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
	}
}
