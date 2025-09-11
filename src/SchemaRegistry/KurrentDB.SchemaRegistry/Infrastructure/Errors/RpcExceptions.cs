// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Kurrent.Rpc;
using KurrentDB.Api.Infrastructure.Errors;
using Enum = System.Enum;
using Status = Google.Rpc.Status;

namespace KurrentDB.Api.Errors;

public static partial class RpcExceptions {
	static RpcException Create(int statusCode, string message, params IMessage[] details) {
		Debug.Assert(statusCode.GetHashCode() != 0, "The status code must not be the default value!");
		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");

		var status = new Status {
			Code    = statusCode,
			Message = message,
			Details = { details.Select(Any.Pack) }
		};

		return status.ToRpcException();
	}

	/// <summary>
	/// Creates a generic RPC exception with the specified status code, message, and optional details.
	/// This method is used internally to construct <see cref="RpcException"/> instances with rich
	/// error information, including structured details that can be unpacked by clients.
	/// Underneath, it creates a <see cref="Google.Rpc.Status"/> message and converts it to an <see cref="RpcException"/>.
	/// The details are packed into the status message using <see cref="Any.Pack(IMessage)"/>.
	/// This ensures that the details are properly serialized and can be deserialized by clients.
	/// The created <see cref="Google.Rpc.Status"/> is added to the metadata with the <c>grpc-status-details-bin</c> key.
	/// </summary>
	static RpcException Create(StatusCode statusCode, string message, params IMessage[] details) =>
		Create((int)statusCode, message, details);

	/// <summary>
	/// Creates a generic RPC exception based on a specific error enum value, message, and optional details.
	/// The error enum must be annotated with <see cref="Kurrent.Rpc.ErrorMetadata"/> to provide
	/// metadata such as the error code, status code, and whether it has details.
	/// This method extracts the metadata from the enum and constructs an <see cref="RpcException"/>
	/// with the appropriate status code and error code in the details.
	/// If the error is annotated to have details, at least one detail of the specified type
	/// must be provided; otherwise, an assertion will fail in debug builds.
	/// The created exception includes a <see cref="RequestErrorInfo"/> detail containing the error
	/// code, along with any additional details provided.
	/// This ensures that clients can programmatically identify the error type and access
	/// any structured details associated with the error.
	/// </summary>
	public static RpcException FromError<TError>(TError error, string message, params IMessage[] details) where TError : struct, Enum {
		Debug.Assert(error.GetHashCode() != 0, "The error must not be the default value!");
		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");

		var err = error.GetErrorMetadata();

		Debug.Assert(
			err.HasDetails && details.Any(d => d.GetType() == err.DetailsType),
			$"The error is annotated to have details of type '{err.DetailsType}', but none were provided!");

		var info = new RequestErrorInfo { Code = err.Code };

		return Create((StatusCode)err.StatusCode, message, details.Prepend(info).ToArray());
	}
}
