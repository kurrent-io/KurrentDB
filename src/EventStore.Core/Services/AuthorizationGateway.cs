// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services;

public sealed class AuthorizationGateway {
	private readonly IAuthorizationProvider _authorizationProvider;
	private const string AccessDenied = "Access Denied";

	private static readonly Func<ClientMessage.ReadEvent, Message> ReadEventDenied = msg =>
		new ClientMessage.ReadEventCompleted(msg.CorrelationId, msg.EventStreamId, ReadEventResult.AccessDenied,
			ResolvedEvent.EmptyEvent, StreamMetadata.Empty, false, AccessDenied);

	private static readonly Func<ClientMessage.ReadAllEventsForward, Message> ReadAllEventsForwardDenied = msg =>
		new ClientMessage.ReadAllEventsForwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied, AccessDenied,
			Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
			TFPos.Invalid, default);

	private static readonly Func<ClientMessage.ReadStreamEventsForward, Message> ReadStreamEventsForwardDenied =
		msg => new ClientMessage.ReadStreamEventsForwardCompleted(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,ReadStreamResult.AccessDenied, Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, AccessDenied, -1, default, true, default);

	private static readonly Func<ClientMessage.ReadStreamEventsBackward, Message> ReadStreamEventsBackwardDenied =
		msg => new ClientMessage.ReadStreamEventsBackwardCompleted(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.AccessDenied,Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, default, AccessDenied, -1, default, true, default);

	private static readonly Func<ClientMessage.WriteEvents, Message> WriteEventsDenied = msg =>
		new ClientMessage.WriteEventsCompleted(msg.CorrelationId, OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<ClientMessage.DeleteStream, Message> DeleteStreamDenied = msg =>
		new ClientMessage.DeleteStreamCompleted(msg.CorrelationId, OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<ClientMessage.SubscribeToStream, Message> SubscribeToStreamDenied = msg =>
		new ClientMessage.SubscriptionDropped(msg.CorrelationId, SubscriptionDropReason.AccessDenied);
	private static readonly Func<ClientMessage.FilteredSubscribeToStream, Message> FilteredSubscribeToStreamDenied =
		msg =>
			new ClientMessage.SubscriptionDropped(msg.CorrelationId, SubscriptionDropReason.AccessDenied);

	private static readonly Func<ClientMessage.ReadAllEventsBackward, Message> ReadAllEventsBackwardDenied = msg =>
		new ClientMessage.ReadAllEventsBackwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied,
			AccessDenied,
			Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
			TFPos.Invalid, default);

	private static readonly Func<ClientMessage.FilteredReadAllEventsForward, Message>
		FilteredReadAllEventsForwardDenied = msg =>
			new ClientMessage.ReadAllEventsForwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied,
				AccessDenied,
				Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
				TFPos.Invalid, default);

	private static readonly Func<ClientMessage.FilteredReadAllEventsBackward, Message>
		FilteredReadAllEventsBackwardDenied = msg =>
			new ClientMessage.ReadAllEventsBackwardCompleted(msg.CorrelationId, ReadAllResult.AccessDenied,
				AccessDenied,
				Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, 0, TFPos.Invalid, TFPos.Invalid,
				TFPos.Invalid, default);

	private static readonly Func<ClientMessage.ConnectToPersistentSubscriptionToStream, Message>
		ConnectToPersistentSubscriptionDenied = msg =>
			new ClientMessage.SubscriptionDropped(msg.CorrelationId, SubscriptionDropReason.AccessDenied);

	private static readonly Func<ClientMessage.ReadNextNPersistentMessages, Message>
		ReadNextNPersistedMessagesDenied = msg =>
			new ClientMessage.ReadNextNPersistentMessagesCompleted(msg.CorrelationId,
				ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult.AccessDenied,
				AccessDenied, Array.Empty<(ResolvedEvent, int)>());

	private static readonly Func<ClientMessage.CreatePersistentSubscriptionToStream, Message>
		CreatePersistentSubscriptionDenied = msg =>
			new ClientMessage.CreatePersistentSubscriptionToStreamCompleted(msg.CorrelationId,
				ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.AccessDenied,
				AccessDenied);

	private static readonly Func<ClientMessage.UpdatePersistentSubscriptionToStream, Message>
		UpdatePersistentSubscriptionDenied = msg =>
			new ClientMessage.UpdatePersistentSubscriptionToStreamCompleted(msg.CorrelationId,
				ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult.AccessDenied,
				AccessDenied);

	private static readonly Func<ClientMessage.ReplayParkedMessages, Message> ReplayAllParkedMessagesDenied =
		msg => new ClientMessage.ReplayMessagesReceived(msg.CorrelationId,
			ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.AccessDenied, AccessDenied);

	private static readonly Func<ClientMessage.DeletePersistentSubscriptionToStream, Message>
		DeletePersistentSubscriptionDenied =
			msg => new ClientMessage.DeletePersistentSubscriptionToStreamCompleted(msg.CorrelationId,
				ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult.AccessDenied,
				AccessDenied);

	private static readonly Func<ClientMessage.TransactionStart, Message> TransactionStartDenied = msg =>
		new ClientMessage.TransactionStartCompleted(msg.CorrelationId, -1, OperationResult.AccessDenied,
			AccessDenied);

	private static readonly Func<ClientMessage.TransactionWrite, Message> TransactionWriteDenied = msg =>
		new ClientMessage.TransactionWriteCompleted(msg.CorrelationId, msg.TransactionId,
			OperationResult.AccessDenied, AccessDenied);

	private static readonly Func<ClientMessage.TransactionCommit, Message> TransactionCommitDenied = msg =>
		new ClientMessage.TransactionCommitCompleted(msg.CorrelationId, msg.TransactionId,
			OperationResult.AccessDenied, AccessDenied);


	private static readonly Operation ReadStream = new Operation(Operations.Streams.Read);
	private static readonly Operation WriteStream = new Operation(Operations.Streams.Write);
	private static readonly Operation DeleteStream = new Operation(Operations.Streams.Delete);
	private static readonly Operation ReadEvent = ReadStream;
	private static readonly Operation FilteredSubscribeToStream = ReadStream;

	private static readonly Operation ReadAllStream =
		new Operation(Operations.Streams.Read).WithParameter(
			Operations.Streams.Parameters.StreamId(SystemStreams.AllStream));

	private static readonly Operation CreatePersistentSubscription = new Operation(Operations.Subscriptions.Create);
	private static readonly Operation UpdatePersistentSubscription = new Operation(Operations.Subscriptions.Update);
	private static readonly Operation DeletePersistentSubscription = new Operation(Operations.Subscriptions.Delete);

	private static readonly Operation
		ReplayAllParkedMessages = new Operation(Operations.Subscriptions.ReplayParked);

	private static readonly Operation ConnectToPersistentSubscription =
		new Operation(Operations.Subscriptions.ProcessMessages);

	public AuthorizationGateway(IAuthorizationProvider authorizationProvider) {
		_authorizationProvider = authorizationProvider;
	}

	public void Authorize(Message toValidate, IPublisher destination) {
		Ensure.NotNull(toValidate, nameof(toValidate));
		Ensure.NotNull(destination, nameof(destination));
		switch (toValidate) {
			case ClientMessage.ReadNextNPersistentMessages msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.ReadStreamEventsBackward msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.ReadStreamEventsForward msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.UpdatePersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.SubscribeToStream msg: 
				Authorize(msg, destination);
				break;
			case ClientMessage.ConnectToPersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.CreatePersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.DeletePersistentSubscriptionToStream msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.DeleteStream msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.FilteredReadAllEventsBackward msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.FilteredReadAllEventsForward msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.FilteredSubscribeToStream msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.ReadAllEventsBackward msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.ReadAllEventsForward msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.ReplayParkedMessages msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.ReadEvent msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.WriteEvents msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.TransactionStart msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.TransactionWrite msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.TransactionCommit msg:
				Authorize(msg, destination);
				break;
			case ClientMessage.PersistentSubscriptionAckEvents _:
			case ClientMessage.PersistentSubscriptionNackEvents _:
			case ClientMessage.UnsubscribeFromStream _:
			case ClientMessage.NotHandled _:
			case ClientMessage.WriteEventsCompleted _:
			case ClientMessage.TransactionStartCompleted _:
			case ClientMessage.TransactionWriteCompleted _:
			case ClientMessage.TransactionCommitCompleted _:
			case ClientMessage.DeleteStreamCompleted _:
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

	private void Authorize(ClientMessage.SubscribeToStream msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, SubscribeToStreamDenied);
	}

	private void Authorize(ClientMessage.ReadEvent msg, IPublisher destination) {
		Authorize(msg.User, ReadEvent.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadEventDenied);
	}

	private void Authorize(ClientMessage.WriteEvents msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, WriteEventsDenied);
	}

	private void Authorize(ClientMessage.ReplayParkedMessages msg, IPublisher destination) {
		Authorize(msg.User, ReplayAllParkedMessages, msg.Envelope, destination, msg, ReplayAllParkedMessagesDenied);
	}

	private void Authorize(ClientMessage.ReadAllEventsForward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, ReadAllEventsForwardDenied);
	}

	private void Authorize(ClientMessage.ReadAllEventsBackward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, ReadAllEventsBackwardDenied);
	}

	private void Authorize(ClientMessage.FilteredSubscribeToStream msg, IPublisher destination) {
		Authorize(msg.User,
			FilteredSubscribeToStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, FilteredSubscribeToStreamDenied);
	}

	private void Authorize(ClientMessage.FilteredReadAllEventsForward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, FilteredReadAllEventsForwardDenied);
	}

	private void Authorize(ClientMessage.FilteredReadAllEventsBackward msg, IPublisher destination) {
		Authorize(msg.User, ReadAllStream, msg.Envelope, destination, msg, FilteredReadAllEventsBackwardDenied);
	}

	private void Authorize(ClientMessage.DeleteStream msg, IPublisher destination) {
		Authorize(msg.User, DeleteStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, DeleteStreamDenied);
	}

	private void Authorize(ClientMessage.DeletePersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User, DeletePersistentSubscription, msg.Envelope, destination, msg,
			DeletePersistentSubscriptionDenied);
	}

	private void Authorize(ClientMessage.CreatePersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User, CreatePersistentSubscription, msg.Envelope, destination, msg,
			CreatePersistentSubscriptionDenied);
	}

	private void Authorize(ClientMessage.ConnectToPersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User,
			ConnectToPersistentSubscription.WithParameter(
				Operations.Subscriptions.Parameters.StreamId(msg.EventStreamId)), msg.Envelope, destination, msg,
			ConnectToPersistentSubscriptionDenied);
	}

	private void Authorize(ClientMessage.UpdatePersistentSubscriptionToStream msg, IPublisher destination) {
		Authorize(msg.User, UpdatePersistentSubscription, msg.Envelope, destination, msg,
			UpdatePersistentSubscriptionDenied);
	}

	private void Authorize(ClientMessage.ReadStreamEventsForward msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadStreamEventsForwardDenied);
	}

	private void Authorize(ClientMessage.ReadStreamEventsBackward msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadStreamEventsBackwardDenied);
	}

	private void Authorize(ClientMessage.ReadNextNPersistentMessages msg, IPublisher destination) {
		Authorize(msg.User, ReadStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, ReadNextNPersistedMessagesDenied);
	}

	private void Authorize(ClientMessage.TransactionStart msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.StreamId(msg.EventStreamId)),
			msg.Envelope, destination, msg, TransactionStartDenied);
	}

	private void Authorize(ClientMessage.TransactionWrite msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.TransactionId(msg.TransactionId)),
			msg.Envelope, destination, msg, TransactionWriteDenied);
	}

	private void Authorize(ClientMessage.TransactionCommit msg, IPublisher destination) {
		Authorize(msg.User, WriteStream.WithParameter(Operations.Streams.Parameters.TransactionId(msg.TransactionId)),
			msg.Envelope, destination, msg, TransactionCommitDenied);
	}


void Authorize<TRequest>(ClaimsPrincipal user, Operation operation, IEnvelope replyTo,
		IPublisher destination, TRequest request, Func<TRequest, Message> createAccessDenied)
		where TRequest : Message {
		var accessCheck = _authorizationProvider.CheckAccessAsync(user, operation, CancellationToken.None);
		if (!accessCheck.IsCompleted)
			AuthorizeAsync(accessCheck, replyTo, destination, request, createAccessDenied);
		else {
			if(accessCheck.Result)
				destination.Publish(request);
			else {
				replyTo.ReplyWith(createAccessDenied(request));
			}
		}
	}

	async void AuthorizeAsync<TRequest>(ValueTask<bool> accessCheck, IEnvelope replyTo, IPublisher destination, TRequest request,
		Func<TRequest, Message> createAccessDenied) where TRequest : Message {
		if (await accessCheck) {
			destination.Publish(request);
		} else {
			replyTo.ReplyWith(createAccessDenied(request));
		}
	}
}
