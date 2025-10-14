// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using static KurrentDB.Core.Messages.UserManagementMessage;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Users : Client.Users.Users.UsersBase {
	private readonly IPublisher _publisher;
	private readonly IAuthorizationProvider _authorizationProvider;

	public Users(IPublisher publisher, IAuthorizationProvider authorizationProvider) {
		_publisher = Ensure.NotNull(publisher);
		_authorizationProvider = Ensure.NotNull(authorizationProvider);
	}

	private static bool HandleErrors<T>(string loginName, Message message, TaskCompletionSource<T> source) {
		if (message is not ResponseMessage response) {
			source.TrySetException(RpcExceptions.UnknownMessage<ResponseMessage>(message));
			return true;
		}

		if (response.Success)
			return false;
		source.TrySetException(response.Error switch {
			Error.Unauthorized => RpcExceptions.AccessDenied(),
			Error.NotFound => RpcExceptions.LoginNotFound(loginName),
			Error.Conflict => RpcExceptions.LoginConflict(loginName),
			Error.TryAgain => RpcExceptions.LoginTryAgain(loginName),
			_ => RpcExceptions.UnknownError(response.Error)
		});
		return true;
	}
}
