// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Api.Infrastructure.Errors;
using KurrentDB.Protocol.V2.Indexes.Errors;

namespace KurrentDB.Api.Errors;

public static partial class ApiErrors {
	public static RpcException IndexNotFound(string userIndexName) => RpcExceptions.FromError(
		error: IndexesError.IndexNotFound,
		message: $"Index '{userIndexName}' does not exist",
		details: new IndexNotFoundErrorDetails { Name = userIndexName });

	public static RpcException IndexAlreadyExists(string userIndexName) => RpcExceptions.FromError(
		error: IndexesError.IndexAlreadyExists,
		message: $"Index '{userIndexName}' already exists",
		details: new IndexAlreadyExistsErrorDetails { Name = userIndexName });

	public static RpcException IndexesNotReady(long currentPosition, long targetPosition) {
		var percent = 100 * ((double)currentPosition / targetPosition);
		return RpcExceptions.FromError(
			error: IndexesError.IndexesNotReady,
			message: $"Indexes are not ready. Current Position {currentPosition:N0}/{targetPosition:N0} ({percent:N2}%)",
			details: new IndexesNotReadyErrorDetails {
				CurrentPosition = currentPosition,
				TargetPosition = targetPosition,
			});
	}
}
