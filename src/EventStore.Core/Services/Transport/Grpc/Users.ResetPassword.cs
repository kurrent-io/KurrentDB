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
	private static readonly Operation ResetOperation = new(Plugins.Authorization.Operations.Users.ResetPassword);

	public override async Task<ResetPasswordResp> ResetPassword(ResetPasswordReq request, ServerCallContext context) {
		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ResetOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var resetPasswordSource = new TaskCompletionSource<bool>();

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new UserManagementMessage.ResetPassword(envelope, user, options.LoginName, options.NewPassword));

		await resetPasswordSource.Task;

		return new ResetPasswordResp();

		void OnMessage(Message message) {
			if (HandleErrors(options.LoginName, message, resetPasswordSource)) return;

			resetPasswordSource.TrySetResult(true);
		}
	}
}
