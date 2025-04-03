// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Client.Users;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using static EventStore.Plugins.Authorization.Operations.Users;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Users {
	private static readonly Operation ChangePasswordOperation = new(Plugins.Authorization.Operations.Users.ChangePassword);

	public override async Task<ChangePasswordResp> ChangePassword(ChangePasswordReq request,
		ServerCallContext context) {
		var options = request.Options;

		var user = context.GetHttpContext().User;
		var changePasswordOperation = ChangePasswordOperation;
		if (user?.Identity?.Name != null) {
			changePasswordOperation = changePasswordOperation.WithParameter(Parameters.User(user.Identity.Name));
		}

		if (!await _authorizationProvider.CheckAccessAsync(user, changePasswordOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var changePasswordSource = new TaskCompletionSource<bool>();
		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new UserManagementMessage.ChangePassword(envelope, user, options.LoginName, options.CurrentPassword, options.NewPassword));

		await changePasswordSource.Task;

		return new();

		void OnMessage(Message message) {
			if (HandleErrors(options.LoginName, message, changePasswordSource)) return;

			changePasswordSource.TrySetResult(true);
		}
	}
}
