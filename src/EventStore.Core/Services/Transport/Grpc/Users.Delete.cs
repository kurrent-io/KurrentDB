// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Client.Users;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Users {
	private static readonly Operation DeleteOperation = new(Plugins.Authorization.Operations.Users.Delete);

	public override async Task<DeleteResp> Delete(DeleteReq request, ServerCallContext context) {
		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, DeleteOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var deleteSource = new TaskCompletionSource<bool>();
		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new UserManagementMessage.Delete(envelope, user, options.LoginName));

		await deleteSource.Task;

		return new DeleteResp();

		void OnMessage(Message message) {
			if (HandleErrors(options.LoginName, message, deleteSource)) return;

			deleteSource.TrySetResult(true);
		}
	}
}
