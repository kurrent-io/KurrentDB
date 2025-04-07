// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client.Users;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Users {
	private static readonly Operation EnableOperation = new Operation(Plugins.Authorization.Operations.Users.Enable);
	public override async Task<EnableResp> Enable(EnableReq request, ServerCallContext context) {
		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, EnableOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		var enableSource = new TaskCompletionSource<bool>();

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new UserManagementMessage.Enable(envelope, user, options.LoginName));

		await enableSource.Task;

		return new EnableResp();

		void OnMessage(Message message) {
			if (HandleErrors(options.LoginName, message, enableSource))
				return;

			enableSource.TrySetResult(true);
		}
	}
}
