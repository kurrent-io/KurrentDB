// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Net;
using System.Text;
using FluentValidation.Results;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Infrastructure.Errors;
using Kurrent.Rpc;
using KurrentDB.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Type = System.Type;

namespace KurrentDB.Api.Errors;

public static partial class ApiErrors {
    static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates an RPC exception indicating that access to a resource or operation was denied.
    /// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.PermissionDenied"/>
    /// when a user lacks sufficient permissions to perform the requested operation.
    /// The exception includes structured error details with scope and username information.
    /// </summary>
    /// <param name="operation">
    /// A friendly name for the operation or action being attempted that requires specific permissions.
    /// This should correspond to the action the user was trying to perform when access was denied.
    /// </param>
    /// <param name="username">
    /// The login name of the user who was denied access. If not provided, the error message will not include username information.
    /// This parameter is optional and can be null if the username is not available or relevant.
    /// Use with caution as it should only be provided if Grpc Enabled Detailed Errors is set to true in the server options.
    /// </param>
    /// <param name="permission">
    /// The fine-grained claim required to perform the operation.
    /// This should correspond to the permission or role needed to access the resource.
    /// This parameter is optional and can be null if the operation name sufficiently describes the required permission.
    /// Use with caution as it should only be provided if Grpc Enabled Detailed Errors is set to true in the server options.
    /// </param>
    /// <returns>
    /// An <see cref="RpcException"/> with status code <see cref="StatusCode.PermissionDenied"/>,
    /// including <see cref="AccessDeniedErrorDetails"/> details with scope and optionally the user login name
    /// if detailed errors are enabled; otherwise, a more generic error without sensitive details.
    /// </returns>
    public static RpcException AccessDenied(string operation, string? username = null, string? permission = null) {
        Debug.Assert(!string.IsNullOrWhiteSpace(operation), "The operation must not be empty!");

        var message = $"Access denied. Insufficient permissions to perform {operation}";

        var details = new AccessDeniedErrorDetails {
            Operation = operation
        };

        if (username is not null)
            details.Username = username;

        if (permission is not null)
            details.Permission = permission;

        return RpcExceptions.FromError(ServerError.AccessDenied, message, details);
    }

    /// <summary>
    /// Creates an RPC exception indicating that access to a resource or operation was denied.
    /// This method is used within a gRPC service context to create an <see cref="RpcException"/>
    /// with status code <see cref="StatusCode.PermissionDenied"/> when a user lacks sufficient permissions
    /// to perform the requested operation. It automatically retrieves the username from the call context
    /// and respects the server's detailed error settings to include or omit sensitive information.
    /// </summary>
    /// <param name="callContext">
    /// The gRPC server call context, providing access to request metadata and user information.
    /// This context is used to extract the current user's identity and server options.
    /// </param>
    /// <param name="operation">
    /// A friendly name override for the operation or action being attempted that requires specific permissions.
    /// If not provided, a default friendly name will be derived from the method in the call context.
    /// </param>
    /// <param name="permission">
    /// The fine-grained claim required to perform the operation.
    /// This should correspond to the permission or role needed to access the resource.
    /// This parameter is optional and can be null if the operation name sufficiently describes the required permission.
    /// Use with caution as it should only be provided if Grpc Enabled Detailed Errors is set to true in the server options.
    /// </param>
    /// <returns>
    /// An <see cref="RpcException"/> with status code <see cref="StatusCode.PermissionDenied"/>,
    /// including <see cref="AccessDeniedErrorDetails"/> details with scope and optionally the user login name
    /// if detailed errors are enabled; otherwise, a more generic error without sensitive details
    /// </returns>
    public static RpcException AccessDenied(ServerCallContext callContext, string? operation = null, string? permission = null) {
        operation ??= callContext.GetFriendlyOperationName();

        var detailedErrorsEnabled = callContext.GetGrpcServiceOptions().EnableDetailedErrors ?? false;

        return detailedErrorsEnabled
            ? AccessDenied(operation, callContext.GetUser().Identity?.Name, permission)
            : AccessDenied(operation);
    }

    /// <summary>
    /// Creates an RPC exception for requests with invalid arguments based on FluentValidation results.
    /// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>
    /// when request validation fails. The exception includes structured error details with field violations.
    /// </summary>
    /// <param name="requestType">
    /// The type of the request being validated.
    /// This is used to include the request type name in the error message for context.
    /// </param>
    /// <param name="validationResult">
    /// A FluentValidation result containing validation failures.
    /// Must contain at least one validation error to be processed.
    /// Each validation failure should have a property name and error message.
    /// </param>
    /// <returns>
    /// An <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>,
    /// including <see cref="BadRequest"/> details with field violations.
    /// </returns>
    public static RpcException InvalidRequest(Type requestType, ValidationResult validationResult) {
        Debug.Assert(requestType != Type.Missing.GetType(), "The validation result must contain at least one error!");
        Debug.Assert(validationResult.Errors.Count > 0, "The validation result must contain at least one error!");

        var violations = validationResult.Errors.Select(static failure => {
            Debug.Assert(!string.IsNullOrWhiteSpace(failure.ErrorMessage), "The error message in the validation failure must not be empty!");

            return new BadRequest.Types.FieldViolation {
                Field       = failure.PropertyName,
                Description = failure.ErrorMessage
            };
        });

        var details = new BadRequest {
            FieldViolations = { violations }
        };

        string message;

        if (validationResult.Errors.Count == 1 && validationResult.Errors.FirstOrDefault() is { } failure)
            message = $"The '{requestType.Name}' is invalid: {failure.ErrorMessage}";
        else
            message = validationResult.Errors.Aggregate(
                new StringBuilder($"The following '{requestType.Name}' fields are invalid:{Environment.NewLine}"),
                static (sb, failure) => sb.AppendLine($"- {failure.PropertyName}: {failure.ErrorMessage}")
            ).ToString();

        return RpcExceptions.FromError(ServerError.BadRequest, message, details);
    }

    /// <summary>
    /// Creates an RPC exception for requests with invalid arguments based on FluentValidation results.
    /// This method is a generic overload that infers the request type from the type parameter.
    /// </summary>
    /// <param name="validationResult">
    /// A FluentValidation result containing validation failures.
    /// Must contain at least one validation error to be processed.
    /// Each validation failure should have a property name and error message.
    /// </param>
    /// <typeparam name="T">
    /// The type of the request being validated.
    /// This is used to include the request type name in the error message for context.
    /// </typeparam>
    /// <returns>
    /// An <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>,
    /// including <see cref="BadRequest"/> details with field violations.
    /// </returns>
    public static RpcException InvalidRequest<T>(ValidationResult validationResult) where T : IMessage =>
        InvalidRequest(typeof(T), validationResult);

    /// <summary>
    /// Creates an RPC exception for requests with invalid arguments from individual validation failures.
    /// This method is an overload that accepts individual validation failures and creates a ValidationResult internally.
    /// </summary>
    /// <param name="requestType">
    /// The type of the request being validated.
    /// This is used to include the request type name in the error message for context.
    /// </param>
    /// <param name="failures">
    /// An array of validation failures representing invalid request arguments.
    /// Each failure should contain a field name and error description.
    /// </param>
    /// <returns>
    /// An <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>,
    /// including <see cref="BadRequest"/> details with field violations.
    /// </returns>
    public static RpcException InvalidRequest(Type requestType, List<ValidationFailure> failures) =>
        InvalidRequest(requestType, new ValidationResult(failures));

	/// <summary>
	/// Creates an RPC exception for operations that exceed their deadline or timeout.
	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.DeadlineExceeded"/>
	/// when operations take longer than expected. The exception includes retry information to guide client behavior.
	/// </summary>
	/// <param name="message">
	/// A detailed message describing the timeout condition.
	/// This should explain what operation timed out and provide context about the timeout.
	/// </param>
	/// <param name="retryAfter">
	/// An optional suggested retry delay. If not provided, defaults to 3 seconds.
	/// This indicates how long clients should wait before retrying the operation.
	/// </param>
	/// <returns>
	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.DeadlineExceeded"/>,
	/// including <see cref="RetryInfo"/> details with the suggested retry delay.
	/// The error message includes both the provided message and retry guidance.
	/// </returns>
	public static RpcException OperationTimeout(string message, TimeSpan? retryAfter = null) {
		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");

		retryAfter ??= DefaultRetryDelay;

		message = $"Operation timed out: {message} "
		        + $"Please try again after {retryAfter.Value.Humanize()}.";

        var details = new RetryInfo { RetryDelay = Duration.FromTimeSpan(retryAfter.Value) };

		return RpcExceptions.FromError(ServerError.OperationTimeout, message, details);
	}

	/// <summary>
	/// Creates an RPC exception indicating that the server is not yet ready to handle requests.
	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>
	/// when the server is in a startup or initialization state and cannot process requests.
	/// The exception includes retry information to guide client behavior.
	/// </summary>
	/// <param name="retryAfter">
	/// An optional suggested retry delay. If not provided, defaults to 3 seconds.
	/// This indicates how long clients should wait before retrying the request.
	/// </param>
	/// <returns>
	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>,
	/// including <see cref="Google.Rpc.RetryInfo"/> details with the suggested retry delay.
	/// The error message explains that the server is not ready and includes retry guidance.
	/// </returns>
	public static RpcException ServerNotReady(TimeSpan? retryAfter = null) {
		retryAfter ??= DefaultRetryDelay;

		var message = $"The server is not yet ready to handle requests. "
		            + $"Please try again after {retryAfter.Value.Humanize()}.";

		var details = new RetryInfo { RetryDelay = Duration.FromTimeSpan(retryAfter.Value) };

		return RpcExceptions.FromError(ServerError.ServerNotReady, message, details);
	}

	/// <summary>
	/// Creates an RPC exception indicating that the server is currently overloaded.
	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>
	/// when the server cannot handle additional requests due to high load or resource constraints.
	/// The exception includes retry information to guide client behavior.
	/// </summary>
	/// <param name="retryAfter">
	/// An optional suggested retry delay. If not provided, defaults to 5 seconds.
	/// This indicates how long clients should wait before retrying the request.
	/// The default is longer than other unavailable conditions due to the need for load to decrease.
	/// </param>
	/// <returns>
	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>,
	/// including <see cref="RetryInfo"/> details with the suggested retry delay.
	/// The error message explains that the server is overloaded and includes retry guidance.
	/// </returns>
	public static RpcException ServerOverloaded(TimeSpan? retryAfter = null) {
        retryAfter ??= DefaultRetryDelay;

		var message = "The server is currently overloaded and cannot handle the request. "
		            + $"Please try again after {retryAfter.Value.Humanize()}.";

		var details = new RetryInfo { RetryDelay = Duration.FromTimeSpan(retryAfter.Value) };

		return RpcExceptions.FromError(ServerError.ServerOverloaded, message, details);
	}

    /// <summary>
    /// Creates an RPC exception indicating that the current node is not the leader in a clustered environment.
    /// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.FailedPrecondition"/>
    /// when write operations are attempted on a non-leader node in a KurrentDB cluster.
    /// The exception includes information about the current leader node.
    /// </summary>
    /// <param name="leaderNodeId">
    /// The unique identifier of the leader node in the cluster.
    /// </param>
    /// <param name="leaderEndpoint">
    /// The network endpoint of the leader node.
    /// </param>
    /// <returns>
    /// An <see cref="RpcException"/> with status code <see cref="StatusCode.FailedPrecondition"/>,
    /// including <see cref="NotLeaderNodeErrorDetails"/> details with leader node information.
    /// </returns>
    public static RpcException NotLeaderNode(Guid leaderNodeId, DnsEndPoint leaderEndpoint) {
		Debug.Assert(leaderNodeId != Guid.Empty, "The leader node ID must not be empty!");
		Debug.Assert(!string.IsNullOrWhiteSpace(leaderEndpoint.Host), "The leader endpoint host must not be empty!");
		Debug.Assert(leaderEndpoint.Port > 0, "The leader endpoint port must be positive!");

		var message = "The server is not the leader node and cannot handle the request. "
		            + "Please retry your request against the leader node directly at "
		            + $"{leaderEndpoint.Host}:{leaderEndpoint.Port}";

		var notLeaderNode = new NotLeaderNodeErrorDetails {
			CurrentLeader = new NotLeaderNodeErrorDetails.Types.NodeInfo {
                NodeId = leaderNodeId.ToString(),
                Host   = leaderEndpoint.Host,
                Port   = leaderEndpoint.Port,
             }
		};

		return RpcExceptions.FromError(ServerError.NotLeaderNode, message, notLeaderNode);
	}

    // public static RpcException NotLeaderNode(NodeSystemInfo leaderInfo) {
    //     Debug.Assert(leaderInfo != NodeSystemInfo.Empty, "The leader info must not be empty!");
    //     return NotLeaderNode(leaderInfo.InstanceId, leaderInfo.HttpEndPoint);
    // }

    public static RpcException NotLeaderNode(ServerCallContext context) {
        var leaderInfo = context.GetLeaderInfo();
        return NotLeaderNode(leaderInfo.InstanceId, leaderInfo.HttpEndPoint);
    }

    /// <summary>
	/// Indicates that an internal server error has occurred.
	/// This method is used to create an <see cref="RpcException"/> with a standardized
	/// message format for internal server errors, including a prompt to contact support.
	/// The provided message is appended to the standard message to give more context about the error.
	/// </summary>
	/// <param name="message">
	/// A detailed message describing the internal server error.
	/// This message should provide additional context about the error that occurred.
	/// It is recommended to include relevant information that can help in diagnosing the issue.
	/// </param>
	public static RpcException InternalServerError(string message) {
		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");

		var statusMessage = $"An internal server error occurred. "
		                  + $"Please contact support if the problem persists.{Environment.NewLine}"
		                  + $"{message}";

		return RpcExceptions.FromError(ServerError.ServerMalfunction, statusMessage);
	}

	/// <summary>
	/// Indicates that an internal server error has occurred due to an exception.
	/// This method is used to create an <see cref="RpcException"/> with a standardized
	/// message format for internal server errors, including a prompt to contact support.
	/// The message from the provided exception is appended to the standard message to give more context about the error.
	/// Additionally, the exception's details are converted to a <see cref="Google.Rpc.DebugInfo"/> message
	/// and included in the error details to aid in debugging.
	/// </summary>
	/// <param name="exception">
	/// The exception that caused the internal server error.
	/// This exception's message will be included in the error message to provide context about the error.
	/// The exception's details will also be converted to a <see cref="Google.Rpc.DebugInfo"/>
	/// and included in the error details to help diagnose the issue.
	/// It is recommended to pass the original exception that triggered the error.
	/// </param>
	/// <param name="message">
	/// An optional detailed message describing the internal server error.
	/// This message should provide additional context about the error that occurred.
	/// If provided, it will be appended to the standard message replacing the exception's message.
	/// It is recommended to include relevant information that can help in diagnosing the issue.
	/// </param>
	public static RpcException InternalServerError(Exception exception, string? message = null) {
		Debug.Assert(exception is not RpcException, "The provided exception is already an RpcException. Don't wrap it again!");

		message = $"An internal server error occurred. "
		        + $"Please contact support if the problem persists.{Environment.NewLine}"
		        + $"{message ?? exception.Message}";

		return RpcExceptions.FromError(ServerError.ServerMalfunction, message, exception.ToRpcDebugInfo());
	}
}
