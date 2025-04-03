// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Messaging;
using EventStore.Client.Projections;
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
