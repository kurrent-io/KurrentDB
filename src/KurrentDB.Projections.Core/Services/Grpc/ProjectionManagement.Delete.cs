// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Projections.Core.Messages;

// ReSharper disable CheckNamespace

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation DeleteOperation = new Operation(Operations.Projections.Delete);
	public override async Task<DeleteResp> Delete(DeleteReq request, ServerCallContext context) {
		var deletedSource = TaskCompletionSourceFactory.CreateDefault<bool>();
		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, DeleteOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		var name = options.Name;
		var deleteCheckpointStream = options.DeleteCheckpointStream;
		var deleteStateStream = options.DeleteStateStream;
		var deleteEmittedStreams = options.DeleteEmittedStreams;
		var runAs = new ProjectionManagementMessage.RunAs(user);

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.Delete(envelope, name, runAs,
			deleteCheckpointStream, deleteStateStream, deleteEmittedStreams));

		await deletedSource.Task;

		return new DeleteResp();

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.Updated:
					deletedSource.TrySetResult(true);
					break;
				case ProjectionManagementMessage.NotFound:
					deletedSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					deletedSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					break;
			}
		}
	}
}
