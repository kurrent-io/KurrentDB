// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using static KurrentDB.Core.Services.Transport.Grpc.RpcExceptions;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation TruncateParkedOperation = new(Plugins.Authorization.Operations.Subscriptions.TruncateParked);

	public override async Task<TruncateParkedResp> TruncateParked(TruncateParkedReq request, ServerCallContext context) {
		var correlationId = Guid.NewGuid();
		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user, TruncateParkedOperation, context.CancellationToken)) {
			throw AccessDenied();
		}

		string streamId = request.Options.StreamOptionCase switch {
			TruncateParkedReq.Types.Options.StreamOptionOneofCase.All => "$all",
			TruncateParkedReq.Types.Options.StreamOptionOneofCase.StreamIdentifier => request.Options.StreamIdentifier,
			_ => throw new InvalidOperationException()
		};

		long? stopAt = request.Options.StopAtOptionCase switch {
			TruncateParkedReq.Types.Options.StopAtOptionOneofCase.StopAt => request.Options.StopAt,
			TruncateParkedReq.Types.Options.StopAtOptionOneofCase.NoLimit => null,
			_ => throw new InvalidOperationException()
		};

		var envelope = new TcsEnvelope<Message>();
		_publisher.Publish(new ClientMessage.TruncateParkedMessages(
			correlationId,
			correlationId,
			envelope,
			streamId,
			request.Options.GroupName,
			stopAt,
			user));

		return await envelope.Task switch {
			ClientMessage.NotHandled notHandled when TryHandleNotHandled(notHandled, out var ex) => throw ex,
			ClientMessage.TruncateParkedMessagesCompleted { Result: ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.Success } =>
				new TruncateParkedResp(),
			ClientMessage.TruncateParkedMessagesCompleted { Result: ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.DoesNotExist } =>
				throw PersistentSubscriptionDoesNotExist(streamId, request.Options.GroupName),
			ClientMessage.TruncateParkedMessagesCompleted { Result: ClientMessage.TruncateParkedMessagesCompleted.TruncateParkedMessagesCompletedResult.AccessDenied } =>
				throw AccessDenied(),
			ClientMessage.TruncateParkedMessagesCompleted completed =>
				throw PersistentSubscriptionFailed(streamId, request.Options.GroupName, completed.Reason),
			var message =>
				throw UnknownMessage<ClientMessage.TruncateParkedMessagesCompleted>(message)
		};
	}
}
