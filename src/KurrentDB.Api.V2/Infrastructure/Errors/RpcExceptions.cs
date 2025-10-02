// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

using Enum   = System.Enum;
using Status = Google.Rpc.Status;

namespace KurrentDB.Api.Infrastructure.Errors;

public static partial class RpcExceptions {
	/// <summary>
	/// Creates a generic RPC exception based on a specific error enum value, message, and optional details.
	/// The error enum must be annotated with <see cref="Kurrent.Rpc.ErrorMetadata"/> to provide
	/// metadata such as the error code, status code, and whether it has details.
	/// This method extracts the metadata from the enum and constructs an <see cref="RpcException"/>
	/// with the appropriate status code and error code in the details.
	/// If the error is annotated to have details, at least one detail of the specified type
	/// must be provided; otherwise, an assertion will fail in debug builds.
	/// The created exception includes a <see cref="ErrorInfo"/> detail containing the error
	/// code, along with any additional details provided.
	/// This ensures that clients can programmatically identify the error type and access
	/// any structured details associated with the error.
	/// </summary>
	public static RpcException FromError<TError>(TError error, string message, params IMessage[] details) where TError : struct, Enum {
		Debug.Assert(error.GetHashCode() != 0, "The error must not be the default value!");
		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");

		var err = error.GetErrorMetadata();

        Debug.Assert(
            !err.HasDetails || err.HasDetails && details.Any(d => d.GetType() == err.DetailsType),
			$"The error is annotated to have details of type '{err.DetailsType}', but none were provided!");

        var errorInfo = new ErrorInfo {
            Reason = err.Code,
            Domain = err.Domain
        };

		return Create(err.StatusCode, message, details.Prepend(errorInfo).ToArray());
	}

    static RpcException Create(int statusCode, string message, params IMessage[] details) {
        Debug.Assert(statusCode.GetHashCode() != 0, "The error status code must not be OK!");
        Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");

        var status = new Status {
            Code    = statusCode,
            Message = message,
            Details = { details.Select(Any.Pack) }
        };

        return status.ToRpcException();
    }
}
