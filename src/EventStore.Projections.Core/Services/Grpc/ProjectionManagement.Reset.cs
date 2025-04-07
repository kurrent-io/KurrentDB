// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation ResetOperation = new Operation(Operations.Projections.Create);
	public override async Task<ResetResp> Reset(ResetReq request, ServerCallContext context) {
		var resetSource = new TaskCompletionSource<bool>();

		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ResetOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		var name = options.Name;
		var runAs = new ProjectionManagementMessage.RunAs(user);

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.Reset(envelope, name, runAs));

		await resetSource.Task;

		return new ResetResp();

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.Updated:
					resetSource.TrySetResult(true);
					break;
				case ProjectionManagementMessage.NotFound:
					resetSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					resetSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					break;
			}
		}
	}
}
