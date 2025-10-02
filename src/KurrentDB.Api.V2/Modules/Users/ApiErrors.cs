// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Diagnostics;
using Grpc.Core;
using KurrentDB.Api.Infrastructure.Errors;
using KurrentDB.Protocol.V2.Users.Errors;

namespace KurrentDB.Api.Errors;

public static partial class ApiErrors {
	public static RpcException UserNotFound(string loginName) {
		Debug.Assert(!string.IsNullOrWhiteSpace(loginName), "The login name cannot be empty!");

		var message = $"User '{loginName}' was not found.";

		return RpcExceptions.FromError(UsersError.UserNotFound, message);
	}

	public static RpcException UserAlreadyExists(string loginName) {
		Debug.Assert(!string.IsNullOrWhiteSpace(loginName), "The login name cannot be empty!");

		var message = $"User '{loginName}' already exists.";

        return RpcExceptions.FromError(UsersError.UserAlreadyExists, message);
	}
}
