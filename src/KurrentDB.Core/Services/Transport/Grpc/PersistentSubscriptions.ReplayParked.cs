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
	private static readonly Operation ReplayParkedOperation = new(Plugins.Authorization.Operations.Subscriptions.ReplayParked);

	public override async Task<ReplayParkedResp> ReplayParked(ReplayParkedReq request, ServerCallContext context) {
		var correlationId = Guid.NewGuid();
		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user, ReplayParkedOperation, context.CancellationToken)) {
			throw AccessDenied();
		}

		string streamId = request.Options.StreamOptionCase switch {
			ReplayParkedReq.Types.Options.StreamOptionOneofCase.All => "$all",
			ReplayParkedReq.Types.Options.StreamOptionOneofCase.StreamIdentifier => request.Options.StreamIdentifier,
			_ => throw new InvalidOperationException()
		};

		long? stopAt = request.Options.StopAtOptionCase switch {
			ReplayParkedReq.Types.Options.StopAtOptionOneofCase.StopAt => request.Options.StopAt,
			ReplayParkedReq.Types.Options.StopAtOptionOneofCase.NoLimit => null,
			_ => throw new InvalidOperationException()
		};

		var envelope = new TcsEnvelope<Message>();
		_publisher.Publish(new ClientMessage.ReplayParkedMessages(
			correlationId,
			correlationId,
			envelope,
			streamId,
			request.Options.GroupName,
			stopAt,
			user));

		return await envelope.Task switch {
			ClientMessage.NotHandled notHandled when TryHandleNotHandled(notHandled, out var ex) => throw ex,
			ClientMessage.ReplayMessagesReceived { Result: ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Success } =>
				new ReplayParkedResp(),
			ClientMessage.ReplayMessagesReceived { Result: ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.DoesNotExist } =>
				throw PersistentSubscriptionDoesNotExist(streamId, request.Options.GroupName),
			ClientMessage.ReplayMessagesReceived { Result: ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.AccessDenied } =>
				throw AccessDenied(),
			ClientMessage.ReplayMessagesReceived completed =>
				throw PersistentSubscriptionFailed(streamId, request.Options.GroupName, completed.Reason),
			var message =>
				throw UnknownMessage<ClientMessage.ReplayMessagesReceived>(message)
		};
	}
}
