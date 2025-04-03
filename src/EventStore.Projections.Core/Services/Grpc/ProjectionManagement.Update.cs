// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Messaging;
using EventStore.Client.Projections;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;
using static EventStore.Client.Projections.UpdateReq.Types.Options;

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation UpdateOperation = new Operation(Operations.Projections.Update);
	public override async Task<UpdateResp> Update(UpdateReq request, ServerCallContext context) {
		var updatedSource = new TaskCompletionSource<bool>();
		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, UpdateOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var name = options.Name;
		var query = options.Query;
		bool? emitEnabled = (options.EmitOptionCase, options.EmitEnabled) switch {
			(EmitOptionOneofCase.EmitEnabled, true) => true,
			(EmitOptionOneofCase.EmitEnabled, false) => false,
			(EmitOptionOneofCase.NoEmitOptions, _) => default,
			_ => throw new InvalidOperationException()
		};
		var runAs = new ProjectionManagementMessage.RunAs(user);

		var envelope = new CallbackEnvelope(OnMessage);
		_publisher.Publish(
			new ProjectionManagementMessage.Command.UpdateQuery(envelope, name, runAs, query,
				emitEnabled));

		await updatedSource.Task;

		return new UpdateResp();

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.Updated:
					updatedSource.TrySetResult(true);
					break;
				case ProjectionManagementMessage.NotFound:
					updatedSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					updatedSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
					break;
			}
		}
	}
}
