// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using static EventStore.Client.PersistentSubscriptions.CreateReq.Types.Settings;
using static EventStore.Core.Messages.ClientMessage.CreatePersistentSubscriptionToStreamCompleted;
using static EventStore.Core.Messages.ClientMessage.CreatePersistentSubscriptionToAllCompleted;
using static EventStore.Core.Services.Transport.Grpc.RpcExceptions;
using StreamOptionOneofCase = EventStore.Client.PersistentSubscriptions.CreateReq.Types.Options.StreamOptionOneofCase;
using RevisionOptionOneofCase = EventStore.Client.PersistentSubscriptions.CreateReq.Types.StreamOptions.RevisionOptionOneofCase;
using AllOptionOneofCase = EventStore.Client.PersistentSubscriptions.CreateReq.Types.AllOptions.AllOptionOneofCase;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation CreateOperation = new(Plugins.Authorization.Operations.Subscriptions.Create);

	public override async Task<CreateResp> Create(CreateReq request, ServerCallContext context) {
		var createPersistentSubscriptionSource = new TaskCompletionSource<CreateResp>();
		var settings = request.Options.Settings;
		var correlationId = Guid.NewGuid();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user,
			    CreateOperation, context.CancellationToken)) {
			throw AccessDenied();
		}

		string streamId;
		string consumerStrategy;
		if (string.IsNullOrEmpty(settings.ConsumerStrategy)) {
#pragma warning disable 612
			/*for backwards compatibility*/
			consumerStrategy = settings.NamedConsumerStrategy.ToString();
#pragma warning restore 612
		} else {
			consumerStrategy = settings.ConsumerStrategy;
		}

		switch (request.Options.StreamOptionCase) {
			case StreamOptionOneofCase.Stream:
			case StreamOptionOneofCase.None: /*for backwards compatibility*/ {
				StreamRevision startRevision;

				if (request.Options.StreamOptionCase == StreamOptionOneofCase.Stream) {
					streamId = request.Options.Stream.StreamIdentifier;
					startRevision = request.Options.Stream.RevisionOptionCase switch {
						RevisionOptionOneofCase.Revision => new StreamRevision(request.Options.Stream.Revision),
						RevisionOptionOneofCase.Start => StreamRevision.Start,
						RevisionOptionOneofCase.End => StreamRevision.End,
						_ => throw InvalidArgument(request.Options.Stream.RevisionOptionCase)
					};
				} else {
#pragma warning disable 612
					/*for backwards compatibility*/
					streamId = request.Options.StreamIdentifier;
					startRevision = new StreamRevision(request.Options.Settings.Revision);
#pragma warning restore 612
				}

				_publisher.Publish(
					new ClientMessage.CreatePersistentSubscriptionToStream(
						correlationId,
						correlationId,
						new CallbackEnvelope(HandleCreatePersistentSubscriptionCompleted),
						streamId,
						request.Options.GroupName,
						settings.ResolveLinks,
						startRevision.ToInt64(),
						settings.MessageTimeoutCase switch {
							MessageTimeoutOneofCase.MessageTimeoutMs => settings.MessageTimeoutMs,
							MessageTimeoutOneofCase.MessageTimeoutTicks => (int)TimeSpan.FromTicks(settings.MessageTimeoutTicks).TotalMilliseconds,
							_ => 0
						},
						settings.ExtraStatistics,
						settings.MaxRetryCount,
						settings.HistoryBufferSize,
						settings.LiveBufferSize,
						settings.ReadBatchSize,
						settings.CheckpointAfterCase switch {
							CheckpointAfterOneofCase.CheckpointAfterMs => settings.CheckpointAfterMs,
							CheckpointAfterOneofCase.CheckpointAfterTicks => (int)TimeSpan.FromTicks(settings.CheckpointAfterTicks).TotalMilliseconds,
							_ => 0
						},
						settings.MinCheckpointCount,
						settings.MaxCheckpointCount,
						settings.MaxSubscriberCount,
						consumerStrategy,
						user));
				break;
			}
			case StreamOptionOneofCase.All:
				var startPosition = request.Options.All.AllOptionCase switch {
					AllOptionOneofCase.Position => new Position(
						request.Options.All.Position.CommitPosition,
						request.Options.All.Position.PreparePosition),
					AllOptionOneofCase.Start => Position.Start,
					AllOptionOneofCase.End => Position.End,
					_ => throw InvalidArgument(request.Options.All.AllOptionCase)
				};
				var filter = request.Options.All.FilterOptionCase switch {
					CreateReq.Types.AllOptions.FilterOptionOneofCase.NoFilter => null,
					CreateReq.Types.AllOptions.FilterOptionOneofCase.Filter => ConvertToEventFilter(true, request.Options.All.Filter),
					CreateReq.Types.AllOptions.FilterOptionOneofCase.None => null,
					_ => throw InvalidArgument(request.Options.All.FilterOptionCase)
				};

				streamId = SystemStreams.AllStream;
				_publisher.Publish(
					new ClientMessage.CreatePersistentSubscriptionToAll(
						correlationId,
						correlationId,
						new CallbackEnvelope(HandleCreatePersistentSubscriptionCompleted),
						request.Options.GroupName,
						filter,
						settings.ResolveLinks,
						new TFPos(startPosition.ToInt64().commitPosition, startPosition.ToInt64().preparePosition),
						settings.MessageTimeoutCase switch {
							MessageTimeoutOneofCase.MessageTimeoutMs => settings.MessageTimeoutMs,
							MessageTimeoutOneofCase.MessageTimeoutTicks => (int)TimeSpan.FromTicks(settings.MessageTimeoutTicks).TotalMilliseconds,
							_ => 0
						},
						settings.ExtraStatistics,
						settings.MaxRetryCount,
						settings.HistoryBufferSize,
						settings.LiveBufferSize,
						settings.ReadBatchSize,
						settings.CheckpointAfterCase switch {
							CheckpointAfterOneofCase.CheckpointAfterMs => settings.CheckpointAfterMs,
							CheckpointAfterOneofCase.CheckpointAfterTicks => (int)TimeSpan.FromTicks(settings.CheckpointAfterTicks).TotalMilliseconds,
							_ => 0
						},
						settings.MinCheckpointCount,
						settings.MaxCheckpointCount,
						settings.MaxSubscriberCount,
						consumerStrategy,
						user));
				break;
			default:
				throw new InvalidOperationException();
		}

		IEventFilter ConvertToEventFilter(bool isAllStream, CreateReq.Types.AllOptions.Types.FilterOptions filter) =>
			filter.FilterCase switch {
				CreateReq.Types.AllOptions.Types.FilterOptions.FilterOneofCase.EventType => (
					string.IsNullOrEmpty(filter.EventType.Regex)
						? EventFilter.EventType.Prefixes(isAllStream, filter.EventType.Prefix.ToArray())
						: EventFilter.EventType.Regex(isAllStream, filter.EventType.Regex)),
				CreateReq.Types.AllOptions.Types.FilterOptions.FilterOneofCase.StreamIdentifier => (
					string.IsNullOrEmpty(filter.StreamIdentifier.Regex)
						? EventFilter.StreamName.Prefixes(isAllStream, filter.StreamIdentifier.Prefix.ToArray())
						: EventFilter.StreamName.Regex(isAllStream, filter.StreamIdentifier.Regex)),
				_ => throw InvalidArgument(filter.FilterCase)
			};

		return await createPersistentSubscriptionSource.Task;

		void HandleCreatePersistentSubscriptionCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
				createPersistentSubscriptionSource.TrySetException(ex);
				return;
			}

			if (streamId != SystemStreams.AllStream) {
				if (message is ClientMessage.CreatePersistentSubscriptionToStreamCompleted completed) {
					switch (completed.Result) {
						case CreatePersistentSubscriptionToStreamResult.Success:
							createPersistentSubscriptionSource.TrySetResult(new CreateResp());
							return;
						case CreatePersistentSubscriptionToStreamResult.Fail:
							createPersistentSubscriptionSource.TrySetException(
								PersistentSubscriptionFailed(streamId, request.Options.GroupName, completed.Reason)
							);
							return;
						case CreatePersistentSubscriptionToStreamResult.AlreadyExists:
							createPersistentSubscriptionSource.TrySetException(PersistentSubscriptionExists(streamId, request.Options.GroupName));
							return;
						case CreatePersistentSubscriptionToStreamResult.AccessDenied:
							createPersistentSubscriptionSource.TrySetException(AccessDenied());
							return;
						default:
							createPersistentSubscriptionSource.TrySetException(UnknownError(completed.Result));
							return;
					}
				}

				createPersistentSubscriptionSource.TrySetException(
					UnknownMessage<ClientMessage.CreatePersistentSubscriptionToStreamCompleted>(message)
				);
			} else {
				if (message is ClientMessage.CreatePersistentSubscriptionToAllCompleted completedAll) {
					switch (completedAll.Result) {
						case CreatePersistentSubscriptionToAllResult.Success:
							createPersistentSubscriptionSource.TrySetResult(new CreateResp());
							return;
						case CreatePersistentSubscriptionToAllResult.Fail:
							createPersistentSubscriptionSource.TrySetException(
								PersistentSubscriptionFailed(streamId, request.Options.GroupName, completedAll.Reason)
							);
							return;
						case CreatePersistentSubscriptionToAllResult.AlreadyExists:
							createPersistentSubscriptionSource.TrySetException(PersistentSubscriptionExists(streamId, request.Options.GroupName));
							return;
						case CreatePersistentSubscriptionToAllResult.AccessDenied:
							createPersistentSubscriptionSource.TrySetException(AccessDenied());
							return;
						default:
							createPersistentSubscriptionSource.TrySetException(UnknownError(completedAll.Result));
							return;
					}
				}

				createPersistentSubscriptionSource.TrySetException(UnknownMessage<ClientMessage.CreatePersistentSubscriptionToAllCompleted>(message));
			}
		}
	}
}
