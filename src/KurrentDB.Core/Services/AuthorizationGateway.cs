// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services;

public sealed class AuthorizationGateway(IAuthorizationProvider authorizationProvider) {
	private const string AccessDenied = "Access Denied";

	private static readonly Func<ReadEvent, Message> ReadEventDenied = msg =>
		new ReadEventCompleted(msg.CorrelationId, msg.EventStreamId, ReadEventResult.AccessDenied,
			ResolvedEvent.EmptyEvent, StreamMetadata.Empty, false, AccessDenied);

	private static readonly Func<ReadAllEventsForward, Message> ReadAllEventsForwardDenied = msg =>
		new ReadAllEventsForwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied, AccessDenied,
			[], StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
			TFPos.Invalid, default);

	private static readonly Func<ReadStreamEventsForward, Message> ReadStreamEventsForwardDenied =
		msg => new ReadStreamEventsForwardCompleted(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.AccessDenied, Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, AccessDenied, -1, default, true, default);

	private static readonly Func<ReadStreamEventsBackward, Message> ReadStreamEventsBackwardDenied =
		msg => new ReadStreamEventsBackwardCompleted(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.AccessDenied, [], StreamMetadata.Empty, default, AccessDenied, -1, default, true, default);

	private static readonly Func<WriteEvents, Message> WriteEventsDenied = msg =>
		new WriteEventsCompleted(msg.CorrelationId, OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<DeleteStream, Message> DeleteStreamDenied = msg =>
		new DeleteStreamCompleted(msg.CorrelationId, OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<SubscribeToStream, Message> SubscribeToStreamDenied = msg =>
		new SubscriptionDropped(msg.CorrelationId, SubscriptionDropReason.AccessDenied);

	private static readonly Func<FilteredSubscribeToStream, Message> FilteredSubscribeToStreamDenied =
		msg => new SubscriptionDropped(msg.CorrelationId, SubscriptionDropReason.AccessDenied);

	private static readonly Func<ReadAllEventsBackward, Message> ReadAllEventsBackwardDenied = msg =>
		new ReadAllEventsBackwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied,
			AccessDenied, [], StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid, TFPos.Invalid, default);

	private static readonly Func<FilteredReadAllEventsForward, Message>
		FilteredReadAllEventsForwardDenied = msg =>
			new ReadAllEventsForwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied,
				AccessDenied, [], StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
				TFPos.Invalid, default);

	private static readonly Func<FilteredReadAllEventsBackward, Message>
		FilteredReadAllEventsBackwardDenied = msg =>
			new ReadAllEventsBackwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied,
				AccessDenied, [], StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
				TFPos.Invalid, default);

	private static readonly Func<ConnectToPersistentSubscriptionToStream, Message>
		ConnectToPersistentSubscriptionDenied = msg =>
			new SubscriptionDropped(msg.CorrelationId, SubscriptionDropReason.AccessDenied);

	private static readonly Func<ReadNextNPersistentMessages, Message>
		ReadNextNPersistedMessagesDenied = msg =>
			new ReadNextNPersistentMessagesCompleted(msg.CorrelationId,
				ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult.AccessDenied,
				AccessDenied, []);

	private static readonly Func<CreatePersistentSubscriptionToStream, Message>
		CreatePersistentSubscriptionDenied = msg =>
			new CreatePersistentSubscriptionToStreamCompleted(msg.CorrelationId,
				CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.AccessDenied,
				AccessDenied);

	private static readonly Func<UpdatePersistentSubscriptionToStream, Message>
		UpdatePersistentSubscriptionDenied = msg =>
			new UpdatePersistentSubscriptionToStreamCompleted(msg.CorrelationId,
				UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult.AccessDenied,
				AccessDenied);

	private static readonly Func<ReplayParkedMessages, Message> ReplayAllParkedMessagesDenied =
		msg => new ReplayMessagesReceived(msg.CorrelationId, ReplayMessagesReceived.ReplayMessagesReceivedResult.AccessDenied, AccessDenied);

	private static readonly Func<DeletePersistentSubscriptionToStream, Message>
		DeletePersistentSubscriptionDenied =
			msg => new DeletePersistentSubscriptionToStreamCompleted(msg.CorrelationId,
				DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult.AccessDenied,
				AccessDenied);

	private static readonly Func<TransactionStart, Message> TransactionStartDenied = msg =>
		new TransactionStartCompleted(msg.CorrelationId, -1, OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<TransactionWrite, Message> TransactionWriteDenied = msg =>
		new TransactionWriteCompleted(msg.CorrelationId, msg.TransactionId, OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<TransactionCommit, Message> TransactionCommitDenied = msg =>
		new TransactionCommitCompleted(msg.CorrelationId, msg.TransactionId, OperationResult.AccessDenied, AccessDenied);

	private static readonly Operation ReadStream = new Operation(Operations.Streams.Read);
	private static readonly Operation WriteStream = new Operation(Operations.Streams.Write);
	private static readonly Operation DeleteStream = new Operation(Operations.Streams.Delete);
	private static readonly Operation ReadEvent = ReadStream;
	private static readonly Operation FilteredSubscribeToStream = ReadStream;

	private static readonly Operation ReadAllStream =
		new Operation(Operations.Streams.Read).WithParameter(Operations.Streams.Parameters.StreamId(SystemStreams.AllStream));

	private static readonly Operation CreatePersistentSubscription = new Operation(Operations.Subscriptions.Create);
	private static readonly Operation UpdatePersistentSubscription = new Operation(Operations.Subscriptions.Update);
	private static readonly Operation DeletePersistentSubscription = new Operation(Operations.Subscriptions.Delete);
	private static readonly Operation ReplayAllParkedMessages = new Operation(Operations.Subscriptions.ReplayParked);
	private static readonly Operation ConnectToPersistentSubscription = new Operation(Operations.Subscriptions.ProcessMessages);

	public void Authorize(Message toValidate, IPublisher destination) {
		Ensure.NotNull(toValidate, nameof(toValidate));
		Ensure.NotNull(destination, nameof(destination));
		switch (toValidate) {
			case ReadNextNPersistentMessages msg:
				Authorize(msg, destination);
				break;
			case ReadStreamEventsBackward msg:
				Authorize(msg, destination);
				break;
			case ReadStreamEventsForward msg:
				Authorize(msg, destination);
				break;
			case UpdatePersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case SubscribeToStream msg:
				Authorize(msg, destination);
				break;
			case ConnectToPersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case CreatePersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case DeletePersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case DeleteStream msg:
				Authorize(msg, destination);
				break;
			case FilteredReadAllEventsBackward msg:
				Authorize(msg, destination);
				break;
			case FilteredReadAllEventsForward msg:
				Authorize(msg, destination);
				break;
			case FilteredSubscribeToStream msg:
				Authorize(msg, destination);
				break;
			case ReadAllEventsBackward msg:
				Authorize(msg, destination);
				break;
			case ReadAllEventsForward msg:
				Authorize(msg, destination);
				break;
			case ReplayParkedMessages msg:
				Authorize(msg, destination);
				break;
			case ReadEvent msg:
				Authorize(msg, destination);
				break;
			case WriteEvents msg:
				Authorize(msg, destination);
				break;
			case TransactionStart msg:
				Authorize(msg, destination);
				break;
			case TransactionWrite msg:
				Authorize(msg, destination);
				break;
			case TransactionCommit msg:
				Authorize(msg, destination);
				break;
			case PersistentSubscriptionAckEvents _:
			case PersistentSubscriptionNackEvents _:
			case UnsubscribeFromStream _:
			case NotHandled _:
			case WriteEventsCompleted _:
			case TransactionStartCompleted _:
			case TransactionWriteCompleted _:
			case TransactionCommitCompleted _:
			case DeleteStreamCompleted _:
				destination.Publish(toValidate);
				break;
			case ReplicationMessage.AckLogPosition _:
			case ReplicationMessage.CloneAssignment _:
			case ReplicationMessage.CreateChunk _:
			case ReplicationMessage.DataChunkBulk _:
			case ReplicationMessage.DropSubscription _:
			case ReplicationMessage.FollowerAssignment _:
			case ReplicationMessage.GetReplicationStats _:
			case ReplicationMessage.GetReplicationStatsCompleted _:
			case ReplicationMessage.RawChunkBulk _:
			case ReplicationMessage.ReconnectToLeader _:
			case ReplicationMessage.ReplicaLogPositionAck _:
			case ReplicationMessage.ReplicaSubscribed _:
			case ReplicationMessage.ReplicaSubscriptionRetry _:
			case ReplicationMessage.ReplicaSubscriptionRequest _:
			case ReplicationMessage.SubscribeReplica _:
			case ReplicationMessage.SubscribeToLeader _:
			case ReplicationTrackingMessage.IndexedTo _:
			case ReplicationTrackingMessage.LeaderReplicatedTo _:
			case ReplicationTrackingMessage.ReplicaWriteAck _:
			case ReplicationTrackingMessage.ReplicatedTo _:
			case ReplicationTrackingMessage.WriterCheckpointFlushed _:
				destination.Publish(toValidate);
				break;
			default:
#if DEBUG
				//This sucks, because if new tcp messages are added there is no way to be sure they have to be authorized...
				//They should be caught by debug builds though
				throw new ArgumentOutOfRangeException(nameof(toValidate), toValidate.GetType().FullName,
					"Unhandled client message");
#else
					destination.Publish(toValidate);
					break;
#endif
		}
	}

	private void Authorize(SubscribeToStream msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, SubscribeToStreamDenied);
	}

	private void Authorize(ReadEvent msg, IPublisher destination) {
		Authorize(msg.User, ReadEvent.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadEventDenied);
	}

	private void Authorize(WriteEvents msg, IPublisher destination) {
		if (msg.EventStreamIds.Length > 1)
			throw new NotSupportedException("Authorization of multi-stream writes is not supported");

		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamIds.Single)),
			msg.Envelope, destination, msg, WriteEventsDenied);
	}

	private void Authorize(ReplayParkedMessages msg, IPublisher destination) {
		Authorize(msg.User, ReplayAllParkedMessages, msg.Envelope, destination, msg, ReplayAllParkedMessagesDenied);
	}

	private void Authorize(ReadAllEventsForward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, ReadAllEventsForwardDenied);
	}

	private void Authorize(ReadAllEventsBackward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, ReadAllEventsBackwardDenied);
	}

	private void Authorize(FilteredSubscribeToStream msg, IPublisher destination) {
		Authorize(msg.User,
			FilteredSubscribeToStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, FilteredSubscribeToStreamDenied);
	}

	private void Authorize(FilteredReadAllEventsForward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, FilteredReadAllEventsForwardDenied);
	}

	private void Authorize(FilteredReadAllEventsBackward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, FilteredReadAllEventsBackwardDenied);
	}

	private void Authorize(DeleteStream msg, IPublisher destination) {
		Authorize(msg.User, DeleteStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, DeleteStreamDenied);
	}

	private void Authorize(DeletePersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User, DeletePersistentSubscription, msg.Envelope, destination, msg, DeletePersistentSubscriptionDenied);
	}

	private void Authorize(CreatePersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User, CreatePersistentSubscription, msg.Envelope, destination, msg, CreatePersistentSubscriptionDenied);
	}

	private void Authorize(ConnectToPersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User,
			ConnectToPersistentSubscription.WithParameter(Operations.Subscriptions.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg,
			ConnectToPersistentSubscriptionDenied);
	}

	private void Authorize(UpdatePersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User, UpdatePersistentSubscription, msg.Envelope, destination, msg,
			UpdatePersistentSubscriptionDenied);
	}

	private void Authorize(ReadStreamEventsForward msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadStreamEventsForwardDenied);
	}

	private void Authorize(ReadStreamEventsBackward msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadStreamEventsBackwardDenied);
	}

	private void Authorize(ReadNextNPersistentMessages msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadNextNPersistedMessagesDenied);
	}

	private void Authorize(TransactionStart msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, TransactionStartDenied);
	}

	private void Authorize(TransactionWrite msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.TransactionId(msg.TransactionId)),
			msg.Envelope, destination, msg, TransactionWriteDenied);
	}

	private void Authorize(TransactionCommit msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.TransactionId(msg.TransactionId)),
			msg.Envelope, destination, msg, TransactionCommitDenied);
	}


	void Authorize<TRequest>(ClaimsPrincipal user, Operation operation, IEnvelope replyTo,
			IPublisher destination, TRequest request, Func<TRequest, Message> createAccessDenied)
			where TRequest : Message {
		var accessCheck = authorizationProvider.CheckAccessAsync(user, operation, CancellationToken.None);
		if (!accessCheck.IsCompleted)
			AuthorizeAsync(accessCheck, replyTo, destination, request, createAccessDenied);
		else {
			if (accessCheck.Result)
				destination.Publish(request);
			else {
				replyTo.ReplyWith(createAccessDenied(request));
			}
		}
	}

	static async void AuthorizeAsync<TRequest>(ValueTask<bool> accessCheck, IEnvelope replyTo, IPublisher destination, TRequest request,
		Func<TRequest, Message> createAccessDenied) where TRequest : Message {
		if (await accessCheck) {
			destination.Publish(request);
		} else {
			replyTo.ReplyWith(createAccessDenied(request));
		}
	}
}
