// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client.Operations;
using KurrentDB.Core.Messages;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

partial class Operations {
	private static readonly Operation StartOperation = new(Plugins.Authorization.Operations.Node.Scavenge.Start);
	private static readonly Operation StopOperation = new(Plugins.Authorization.Operations.Node.Scavenge.Stop);

	public override async Task<ScavengeResp> StartScavenge(StartScavengeReq request, ServerCallContext context) {
		var scavengeResultSource = new TaskCompletionSource<(string, ScavengeResp.Types.ScavengeResult)>();

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, StartOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(
			new ClientMessage.ScavengeDatabase(
				new CallbackEnvelope(OnMessage),
				Guid.NewGuid(),
				user,
				request.Options.StartFromChunk,
				request.Options.ThreadCount,
				null,
				null,
				false
			)
		);

		var (scavengeId, scavengeResult) = await scavengeResultSource.Task;

		return new ScavengeResp {
			ScavengeId = scavengeId,
			ScavengeResult = scavengeResult
		};

		void OnMessage(Message message) => HandleScavengeDatabaseResponse(message, scavengeResultSource);
	}

	public override async Task<ScavengeResp> StopScavenge(StopScavengeReq request, ServerCallContext context) {
		var scavengeResultSource = new TaskCompletionSource<(string, ScavengeResp.Types.ScavengeResult)>();

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, StopOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new ClientMessage.StopDatabaseScavenge(new CallbackEnvelope(OnMessage), Guid.NewGuid(), user, request.Options.ScavengeId));

		var (scavengeId, scavengeResult) = await scavengeResultSource.Task;

		return new ScavengeResp {
			ScavengeId = scavengeId,
			ScavengeResult = scavengeResult
		};

		void OnMessage(Message message) => HandleScavengeDatabaseResponse(message, scavengeResultSource);
	}

	private static void HandleScavengeDatabaseResponse(
		Message message,
		TaskCompletionSource<(string, ScavengeResp.Types.ScavengeResult)> scavengeResultSource) {
		switch (message) {
			case ClientMessage.ScavengeDatabaseUnauthorizedResponse:
				scavengeResultSource.TrySetException(RpcExceptions.AccessDenied());
				return;
			case ClientMessage.ScavengeDatabaseNotFoundResponse notFoundResponse:
				scavengeResultSource.TrySetException(RpcExceptions.ScavengeNotFound(notFoundResponse.ScavengeId));
				return;
			case ClientMessage.ScavengeDatabaseStartedResponse startedResponse:
				scavengeResultSource.TrySetResult((startedResponse.ScavengeId, ScavengeResp.Types.ScavengeResult.Started));
				return;
			case ClientMessage.ScavengeDatabaseStoppedResponse stoppedResponse:
				scavengeResultSource.TrySetResult((stoppedResponse.ScavengeId, ScavengeResp.Types.ScavengeResult.Stopped));
				return;
			case ClientMessage.ScavengeDatabaseInProgressResponse inProgressResponse:
				scavengeResultSource.TrySetResult((inProgressResponse.ScavengeId, ScavengeResp.Types.ScavengeResult.InProgress));
				return;
			default:
				scavengeResultSource.TrySetException(RpcExceptions.UnknownMessage<Message>(message));
				return;
		}
	}
}
