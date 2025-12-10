// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Api.Infrastructure.Errors;
using KurrentDB.Protocol.V2.CustomIndexes.Errors;

namespace KurrentDB.Api.Errors;

public static partial class ApiErrors {
	public static RpcException CustomIndexNotFound(string customIndexName) => RpcExceptions.FromError(
		error: CustomIndexesError.CustomIndexNotFound,
		message: $"Custom Index '{customIndexName}' does not exist",
		details: new CustomIndexNotFoundErrorDetails { Name = customIndexName });

	public static RpcException CustomIndexAlreadyExists(string customIndexName) => RpcExceptions.FromError(
		error: CustomIndexesError.CustomIndexAlreadyExists,
		message: $"Custom Index '{customIndexName}' already exists",
		details: new CustomIndexAlreadyExistsErrorDetails { Name = customIndexName });

	public static RpcException CustomIndexesNotReady(long currentPosition, long targetPosition) {
		var percent = 100 * ((double)currentPosition / targetPosition);
		return RpcExceptions.FromError(
			error: CustomIndexesError.CustomIndexesNotReady,
			message: $"Custom indexes are not ready. Current Position {currentPosition:N0}/{targetPosition:N0} ({percent:N2}%)",
			details: new CustomIndexesNotReadyErrorDetails {
				CurrentPosition = currentPosition,
				TargetPosition = targetPosition,
			});
	}
}
