// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Projections.Core.Messages;

// ReSharper disable CheckNamespace

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation ResetOperation = new(Operations.Projections.Create);

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
