// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Messaging;
using EventStore.Client;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation RestartOperation = new Operation(Operations.Projections.Restart);

	public override async Task<Empty> RestartSubsystem(Empty empty, ServerCallContext context) {
		var restart = new TaskCompletionSource<bool>();
		var envelope = new CallbackEnvelope(OnMessage);

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, RestartOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new ProjectionSubsystemMessage.RestartSubsystem(envelope));

		await restart.Task;
		return new Empty();

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionSubsystemMessage.SubsystemRestarting _:
					restart.TrySetResult(true);
					break;
				case ProjectionSubsystemMessage.InvalidSubsystemRestart fail:
					restart.TrySetException(InvalidSubsystemRestart(fail.SubsystemState));
					break;
				default:
					restart.TrySetException(
						UnknownMessage<ProjectionSubsystemMessage.SubsystemRestarting>(message));
					break;
			}
		}
	}
}
