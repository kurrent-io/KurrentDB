// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using EventStore.Client.Messages;
using Google.Protobuf;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Authentication.DelegatedAuthentication;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using OperationResult = KurrentDB.Core.Messages.OperationResult;

namespace KurrentDB.Core.Services.Transport.Tcp;

public enum ClientVersion : byte {
	V1 = 0,
	V2 = 1
}

public class ClientWriteTcpDispatcher : TcpDispatcher {
	private readonly TimeSpan _writeTimeout;

	protected ClientWriteTcpDispatcher(TimeSpan writeTimeout) {
		_writeTimeout = writeTimeout;
		AddUnwrapper(TcpCommand.WriteEvents, UnwrapWriteEvents, ClientVersion.V2);
		AddUnwrapper(TcpCommand.WriteEventsCompleted, UnwrapWriteEventsCompleted, ClientVersion.V2);
		AddUnwrapper(TcpCommand.WriteEventsMultiStream, UnwrapWriteEventsMultiStream, ClientVersion.V2);
		AddUnwrapper(TcpCommand.WriteEventsMultiStreamCompleted, UnwrapWriteEventsMultiStreamCompleted, ClientVersion.V2);
		AddWrapper<ClientMessage.WriteEvents>(WrapWriteEvents, ClientVersion.V2);
		AddWrapper<ClientMessage.WriteEventsCompleted>(WrapWriteEventsCompleted, ClientVersion.V2);

		AddUnwrapper(TcpCommand.TransactionStart, UnwrapTransactionStart, ClientVersion.V2);
		AddWrapper<ClientMessage.TransactionStart>(WrapTransactionStart, ClientVersion.V2);
		AddUnwrapper(TcpCommand.TransactionStartCompleted, UnwrapTransactionStartCompleted, ClientVersion.V2);
		AddWrapper<ClientMessage.TransactionStartCompleted>(WrapTransactionStartCompleted, ClientVersion.V2);

		AddUnwrapper(TcpCommand.TransactionWrite, UnwrapTransactionWrite, ClientVersion.V2);
		AddWrapper<ClientMessage.TransactionWrite>(WrapTransactionWrite, ClientVersion.V2);
		AddUnwrapper(TcpCommand.TransactionWriteCompleted, UnwrapTransactionWriteCompleted, ClientVersion.V2);
		AddWrapper<ClientMessage.TransactionWriteCompleted>(WrapTransactionWriteCompleted, ClientVersion.V2);

		AddUnwrapper(TcpCommand.TransactionCommit, UnwrapTransactionCommit, ClientVersion.V2);
		AddWrapper<ClientMessage.TransactionCommit>(WrapTransactionCommit, ClientVersion.V2);
		AddUnwrapper(TcpCommand.TransactionCommitCompleted, UnwrapTransactionCommitCompleted, ClientVersion.V2);
		AddWrapper<ClientMessage.TransactionCommitCompleted>(WrapTransactionCommitCompleted, ClientVersion.V2);

		AddUnwrapper(TcpCommand.DeleteStream, UnwrapDeleteStream, ClientVersion.V2);
		AddWrapper<ClientMessage.DeleteStream>(WrapDeleteStream, ClientVersion.V2);
		AddUnwrapper(TcpCommand.DeleteStreamCompleted, UnwrapDeleteStreamCompleted, ClientVersion.V2);
		AddWrapper<ClientMessage.DeleteStreamCompleted>(WrapDeleteStreamCompleted, ClientVersion.V2);
	}

	private ClientMessage.WriteEvents UnwrapWriteEvents(TcpPackage package, IEnvelope envelope,
		ClaimsPrincipal user) {
		var dto = package.Data.Deserialize<WriteEvents>();
		if (dto == null)
			return null;

		var events = new Event[dto.Events.Count];
		for (int i = 0; i < events.Length; ++i) {
			// ReSharper disable PossibleNullReferenceException
			var e = dto.Events[i];
			// ReSharper restore PossibleNullReferenceException
			events[i] = new Event(new Guid(e.EventId.ToByteArray()), e.EventType, e.DataContentType == 1,
				e.Data.ToByteArray(), false, e.Metadata.ToByteArray());
		}

		var cts = new CancellationTokenSource();
		var envelopeWrapper = new CallbackEnvelope(OnMessage);
		cts.CancelAfter(_writeTimeout);

		return ClientMessage.WriteEvents.ForSingleStream(Guid.NewGuid(), package.CorrelationId, envelopeWrapper, dto.RequireLeader,
			dto.EventStreamId, dto.ExpectedVersion, events, user, package.Tokens, cts.Token);

		void OnMessage(Message m) {
			cts.Dispose();
			envelope.ReplyWith(m);
		}
	}

	private ClientMessage.WriteEvents UnwrapWriteEventsMultiStream(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user) {
		var dto = package.Data.Deserialize<WriteEventsMultiStream>();
		if (dto == null)
			return null;

		var events = new Event[dto.Events.Count];
		for (int i = 0; i < events.Length; ++i) {
			// ReSharper disable PossibleNullReferenceException
			var e = dto.Events[i];
			// ReSharper restore PossibleNullReferenceException
			events[i] = new Event(new Guid(e.EventId.ToByteArray()), e.EventType, e.DataContentType == 1,
				e.Data.ToByteArray(), false, e.Metadata.ToByteArray());
		}

		var cts = new CancellationTokenSource();
		var envelopeWrapper = new CallbackEnvelope(OnMessage);
		cts.CancelAfter(_writeTimeout);

		return new ClientMessage.WriteEvents(
			Guid.NewGuid(),
			package.CorrelationId,
			envelopeWrapper,
			dto.RequireLeader,
			eventStreamIds: dto.EventStreamIds.ToLowAllocReadOnlyMemory(),
			expectedVersions: dto.ExpectedVersions.ToLowAllocReadOnlyMemory(),
			events: events,
			eventStreamIndexes: dto.EventStreamIndexes.ToLowAllocReadOnlyMemory(),
			user,
			package.Tokens,
			cts.Token);

		void OnMessage(Message m) {
			cts.Dispose();
			envelope.ReplyWith(m);
		}
	}

	private static TcpPackage WrapWriteEvents(ClientMessage.WriteEvents msg) {
		var events = new NewEvent[msg.Events.Length];
		for (int i = 0; i < events.Length; ++i) {
			var e = msg.Events.Span[i];
			events[i] = new NewEvent(e.EventId.ToByteArray(),
				e.EventType,
				e.IsJson ? 1 : 0,
				0, e.Data,
				e.Metadata);
		}

		if (msg.EventStreamIds.Length <= 1) {
			var dtoSingleStream = new WriteEvents(msg.EventStreamIds.Single, msg.ExpectedVersions.Single, events,
				msg.RequireLeader);
			return CreateWriteRequestPackage(TcpCommand.WriteEvents, msg, dtoSingleStream);
		}

		var dtoMultiStream = new WriteEventsMultiStream(msg.EventStreamIds.Span, msg.ExpectedVersions.Span, events, msg.EventStreamIndexes.Span, msg.RequireLeader);
		return CreateWriteRequestPackage(TcpCommand.WriteEventsMultiStream, msg, dtoMultiStream);

	}

	private static TcpPackage CreateWriteRequestPackage<T>(TcpCommand command, ClientMessage.WriteRequestMessage msg, T dto) where T : IMessage<T> {
		// we are forwarding with InternalCorrId, not client's CorrelationId!!!
		if (msg.User == SystemAccounts.System) {
			return new TcpPackage(command, TcpFlags.TrustedWrite, msg.InternalCorrId, null, null, dto.Serialize());
		}

		foreach (var identity in msg.User.Identities) {
			if (identity is not DelegatedClaimsIdentity dci) {
				continue;
			}

			var jwtClaim = dci.FindFirst("jwt");
			if (jwtClaim != null) {
				return new(command, TcpFlags.Authenticated, msg.InternalCorrId, jwtClaim.Value, dto.Serialize());
			}

			var uidClaim = dci.FindFirst("uid");
			var pwdClaim = dci.FindFirst("pwd");

			if (uidClaim != null && pwdClaim != null) {
				return new(command, TcpFlags.Authenticated, msg.InternalCorrId, uidClaim.Value, pwdClaim.Value, dto.Serialize());
			}
		}

		return msg.Login != null && msg.Password != null
			? new(command, TcpFlags.Authenticated, msg.InternalCorrId, msg.Login, msg.Password, dto.Serialize())
			: new TcpPackage(command, TcpFlags.None, msg.InternalCorrId, null, null, dto.Serialize());
	}

	private static ClientMessage.WriteEventsCompleted UnwrapWriteEventsCompleted(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<WriteEventsCompleted>();
		if (dto == null)
			return null;

		if (dto.Result == EventStore.Client.Messages.OperationResult.Success)
			return new ClientMessage.WriteEventsCompleted(
				package.CorrelationId,
				new(dto.FirstEventNumber),
				new(dto.LastEventNumber),
				dto.PreparePosition,
				dto.CommitPosition);

		return new ClientMessage.WriteEventsCompleted(
			package.CorrelationId,
			(OperationResult)dto.Result,
			dto.Message,
			new(0),
			new(dto.CurrentVersion));
	}

	private static ClientMessage.WriteEventsCompleted UnwrapWriteEventsMultiStreamCompleted(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<WriteEventsMultiStreamCompleted>();
		if (dto == null)
			return null;

		if (dto.Result == EventStore.Client.Messages.OperationResult.Success)
			return new ClientMessage.WriteEventsCompleted(
				package.CorrelationId,
				dto.FirstEventNumbers.ToLowAllocReadOnlyMemory(),
				dto.LastEventNumbers.ToLowAllocReadOnlyMemory(),
				dto.PreparePosition,
				dto.CommitPosition);

		return new ClientMessage.WriteEventsCompleted(
			package.CorrelationId,
			(OperationResult)dto.Result,
			dto.Message,
			dto.FailureStreamIndexes.ToLowAllocReadOnlyMemory(),
			dto.FailureCurrentVersions.ToLowAllocReadOnlyMemory());
	}

	private static TcpPackage WrapWriteEventsCompleted(ClientMessage.WriteEventsCompleted msg) {
		if (msg.Result is OperationResult.Success) {
			return msg.FirstEventNumbers.Length <= 1 ?
				WrapWriteEventsCompletedForSingleStream(msg) :
				WrapWriteEventsCompletedForMultiStream(msg);
		}

		// todo: currently, it's not straightforward to determine if the original write was a multi-stream write when there is a failure
		// the following cases can also happen during a multi-stream write but it doesn't matter as long as the result is unwrapped properly
		// on the other side

		if (msg.FailureStreamIndexes.Span.IsEmpty || msg.FailureStreamIndexes.Span is [ 0 ])
			return WrapWriteEventsCompletedForSingleStream(msg);

		return WrapWriteEventsCompletedForMultiStream(msg);
	}

	private static TcpPackage WrapWriteEventsCompletedForSingleStream(ClientMessage.WriteEventsCompleted msg) {
		var dto = new WriteEventsCompleted((EventStore.Client.Messages.OperationResult)msg.Result,
			msg.Message,
			msg.FirstEventNumbers.Length is 1
				? msg.FirstEventNumbers.Single
				: EventNumber.Invalid, /* for backwards compatibility */
			msg.LastEventNumbers.Length is 1
				? msg.LastEventNumbers.Single
				: EventNumber.Invalid, /* for backwards compatibility */
			msg.PreparePosition,
			msg.CommitPosition,
			msg.FailureCurrentVersions.Length is 1
				? msg.FailureCurrentVersions.Single
				: msg.Result is OperationResult.Success ? 0L : -1L /* for backwards compatibility */);
		return new(TcpCommand.WriteEventsCompleted, msg.CorrelationId, dto.Serialize());
	}

	private static TcpPackage WrapWriteEventsCompletedForMultiStream(ClientMessage.WriteEventsCompleted msg) {
		var dto = new WriteEventsMultiStreamCompleted(
			(EventStore.Client.Messages.OperationResult)msg.Result,
			msg.Message,
			msg.FirstEventNumbers.Span,
			msg.LastEventNumbers.Span,
			msg.PreparePosition,
			msg.CommitPosition,
			msg.FailureCurrentVersions.Span,
			msg.FailureStreamIndexes.Span);
		return new(TcpCommand.WriteEventsMultiStreamCompleted, msg.CorrelationId, dto.Serialize());
	}

	private static ClientMessage.TransactionStart UnwrapTransactionStart(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user) {
		var dto = package.Data.Deserialize<TransactionStart>();
		if (dto == null)
			return null;
		return new(Guid.NewGuid(), package.CorrelationId, envelope,
			dto.RequireLeader,
			dto.EventStreamId, dto.ExpectedVersion, user, package.Tokens);
	}

	private static TcpPackage WrapTransactionStart(ClientMessage.TransactionStart msg) {
		var dto = new TransactionStart(msg.EventStreamId, msg.ExpectedVersion, msg.RequireLeader);
		return CreateWriteRequestPackage(TcpCommand.TransactionStart, msg, dto);
	}

	private static ClientMessage.TransactionStartCompleted UnwrapTransactionStartCompleted(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<TransactionStartCompleted>();
		if (dto == null)
			return null;
		return new(package.CorrelationId, dto.TransactionId, (OperationResult)dto.Result, dto.Message);
	}

	private static TcpPackage WrapTransactionStartCompleted(ClientMessage.TransactionStartCompleted msg) {
		var dto = new TransactionStartCompleted(msg.TransactionId, (EventStore.Client.Messages.OperationResult)msg.Result, msg.Message);
		return new(TcpCommand.TransactionStartCompleted, msg.CorrelationId, dto.Serialize());
	}

	private static ClientMessage.TransactionWrite UnwrapTransactionWrite(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user) {
		var dto = package.Data.Deserialize<TransactionWrite>();
		if (dto == null)
			return null;

		var events = new Event[dto.Events.Count];
		for (int i = 0; i < events.Length; ++i) {
			// ReSharper disable PossibleNullReferenceException
			var e = dto.Events[i];
			// ReSharper restore PossibleNullReferenceException
			events[i] = new Event(new Guid(e.EventId.ToByteArray()), e.EventType, e.DataContentType == 1,
				e.Data.ToByteArray(), false, e.Metadata.ToByteArray());
		}

		return new(Guid.NewGuid(), package.CorrelationId, envelope, dto.RequireLeader, dto.TransactionId, events, user, package.Tokens);
	}

	private static TcpPackage WrapTransactionWrite(ClientMessage.TransactionWrite msg) {
		var events = new NewEvent[msg.Events.Length];
		for (int i = 0; i < events.Length; ++i) {
			var e = msg.Events[i];
			events[i] = new(e.EventId.ToByteArray(), e.EventType, e.IsJson ? 1 : 0, 0, e.Data, e.Metadata);
		}

		var dto = new TransactionWrite(msg.TransactionId, events, msg.RequireLeader);
		return CreateWriteRequestPackage(TcpCommand.TransactionWrite, msg, dto);
	}

	private static ClientMessage.TransactionWriteCompleted UnwrapTransactionWriteCompleted(TcpPackage package,
		IEnvelope envelope) {
		var dto = package.Data.Deserialize<TransactionWriteCompleted>();
		if (dto == null)
			return null;
		return new(package.CorrelationId, dto.TransactionId, (OperationResult)dto.Result, dto.Message);
	}

	private static TcpPackage WrapTransactionWriteCompleted(ClientMessage.TransactionWriteCompleted msg) {
		var dto = new TransactionWriteCompleted(msg.TransactionId, (EventStore.Client.Messages.OperationResult)msg.Result, msg.Message);
		return new(TcpCommand.TransactionWriteCompleted, msg.CorrelationId, dto.Serialize());
	}

	private static ClientMessage.TransactionCommit UnwrapTransactionCommit(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user) {
		var dto = package.Data.Deserialize<TransactionCommit>();
		if (dto == null)
			return null;
		return new(Guid.NewGuid(), package.CorrelationId, envelope, dto.RequireLeader, dto.TransactionId, user, package.Tokens);
	}

	private static TcpPackage WrapTransactionCommit(ClientMessage.TransactionCommit msg) {
		var dto = new TransactionCommit(msg.TransactionId, msg.RequireLeader);
		return CreateWriteRequestPackage(TcpCommand.TransactionCommit, msg, dto);
	}

	private static ClientMessage.TransactionCommitCompleted UnwrapTransactionCommitCompleted(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<TransactionCommitCompleted>();
		if (dto == null)
			return null;
		return dto.Result == EventStore.Client.Messages.OperationResult.Success
			? new(package.CorrelationId, dto.TransactionId, dto.FirstEventNumber, dto.LastEventNumber, dto.PreparePosition, dto.CommitPosition)
			: new(package.CorrelationId, dto.TransactionId, (OperationResult)dto.Result, dto.Message);
	}

	private static TcpPackage WrapTransactionCommitCompleted(ClientMessage.TransactionCommitCompleted msg) {
		var dto = new TransactionCommitCompleted(
			msg.TransactionId,
			(EventStore.Client.Messages.OperationResult)msg.Result,
			msg.Message, msg.FirstEventNumber,
			msg.LastEventNumber,
			msg.PreparePosition,
			msg.CommitPosition);
		return new(TcpCommand.TransactionCommitCompleted, msg.CorrelationId, dto.Serialize());
	}

	private ClientMessage.DeleteStream UnwrapDeleteStream(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user) {
		var dto = package.Data.Deserialize<DeleteStream>();
		if (dto == null)
			return null;

		var cts = new CancellationTokenSource();
		var envelopeWrapper = new CallbackEnvelope(OnMessage);
		cts.CancelAfter(_writeTimeout);

		return new(Guid.NewGuid(), package.CorrelationId, envelopeWrapper, dto.RequireLeader,
			dto.EventStreamId, dto.ExpectedVersion, dto.HardDelete, user, package.Tokens, cts.Token);

		void OnMessage(Message m) {
			cts.Dispose();
			envelope.ReplyWith(m);
		}
	}

	private static TcpPackage WrapDeleteStream(ClientMessage.DeleteStream msg) {
		var dto = new DeleteStream(msg.EventStreamId, msg.ExpectedVersion, msg.RequireLeader, msg.HardDelete);
		return CreateWriteRequestPackage(TcpCommand.DeleteStream, msg, dto);
	}

	private static ClientMessage.DeleteStreamCompleted UnwrapDeleteStreamCompleted(TcpPackage package, IEnvelope envelope) {
		var dto = package.Data.Deserialize<DeleteStreamCompleted>();
		if (dto == null)
			return null;
		return new(package.CorrelationId, (OperationResult)dto.Result, dto.Message, dto.CurrentVersion, dto.PreparePosition, dto.CommitPosition);
	}

	private static TcpPackage WrapDeleteStreamCompleted(ClientMessage.DeleteStreamCompleted msg) {
		var dto = new DeleteStreamCompleted((EventStore.Client.Messages.OperationResult)msg.Result, msg.Message, msg.CurrentVersion, msg.PreparePosition, msg.CommitPosition);
		return new(TcpCommand.DeleteStreamCompleted, msg.CorrelationId, dto.Serialize());
	}
}
