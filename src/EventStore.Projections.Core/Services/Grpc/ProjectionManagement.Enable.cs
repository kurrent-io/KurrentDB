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
	private static readonly Operation EnableOperation = new Operation(Operations.Projections.Enable);
	public override async Task<EnableResp> Enable(EnableReq request, ServerCallContext context) {
		var enableSource = new TaskCompletionSource<bool>();

		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, EnableOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		var name = options.Name;
		var runAs = new ProjectionManagementMessage.RunAs(user);

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.Enable(envelope, name, runAs));

		await enableSource.Task;

		return new EnableResp();

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.Updated:
					enableSource.TrySetResult(true);
					break;
				case ProjectionManagementMessage.NotFound:
					enableSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					enableSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					break;
			}
		}
	}
}
