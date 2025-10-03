// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// // ReSharper disable CheckNamespace
//
// using System.Diagnostics;
// using System.Net;
// using FluentValidation.Results;
// using Google.Protobuf;
// using Grpc.Core;
// using Humanizer;
// using KurrentDB.Protocol.V2.Common.Errors;
//
// namespace KurrentDB.Api.Errors;
//
// public static partial class ApiErrors {
// 	/// <summary>
// 	/// Creates an RPC exception indicating that access to a resource or operation was denied.
// 	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.PermissionDenied"/>
// 	/// when a user lacks sufficient permissions to perform the requested operation.
// 	/// The exception includes structured error details with scope and username information.
// 	/// </summary>
// 	/// <param name="scope">
// 	/// The operation or resource that access was denied to.
// 	/// This should specify what the user was trying to access (e.g., "read stream", "write to stream").
// 	/// </param>
// 	/// <param name="username">
// 	/// The username of the user who was denied access. If not provided, the error message will not include username information.
// 	/// This parameter is optional and can be null if the username is not available or relevant.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.PermissionDenied"/>,
// 	/// including <see cref="AccessDeniedErrorDetails"/> details with scope and username information.
// 	/// </returns>
// 	public static RpcException AccessDenied(string scope, string? username = null) {
// 		Debug.Assert(!string.IsNullOrWhiteSpace(scope), "The scope must not be empty!");
//
// 		var message = $"The user{(username is not null ? $" {username}" : "")} does not have" +
// 		              $" sufficient permissions to perform the operation: '{scope}'";
//
// 		var details = new AccessDeniedErrorDetails {
// 			Scope    = scope,
// 			Username = username,
// 		};
//
// 		return RpcExceptions.FromError(CommonError.AccessDenied, message, details);
// 	}
//
// 	/// <summary>
// 	/// Creates an RPC exception for requests with invalid arguments based on FluentValidation results.
// 	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>
// 	/// when request validation fails. The exception includes structured error details with field violations.
// 	/// </summary>
// 	/// <param name="validationResult">
// 	/// A FluentValidation result containing validation failures.
// 	/// Must contain at least one validation error to be processed.
// 	/// Each validation failure should have a property name and error message.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>,
// 	/// including <see cref="InvalidRequestErrorDetails"/> details with field violations.
// 	/// The error message format depends on the number of violations:
// 	/// - Single violation: "The argument '{field}' is invalid: {description}"
// 	/// - Multiple violations: Lists all violations with field names and descriptions.
// 	/// </returns>
// 	public static RpcException InvalidRequest(ValidationResult validationResult) {
// 		Debug.Assert(validationResult.Errors.Count > 0, "The validation result must contain at least one error!");
//
// 		var violations = validationResult.Errors.Select(failure => {
// 			Debug.Assert(!string.IsNullOrWhiteSpace(failure.PropertyName), "The property name in the validation failure must not be empty!");
// 			Debug.Assert(!string.IsNullOrWhiteSpace(failure.ErrorMessage), "The error message in the validation failure must not be empty!");
//
// 			return new InvalidRequestErrorDetails.Types.FieldViolation {
// 				Field       = failure.PropertyName,
// 				Description = failure.ErrorMessage
// 			};
// 		});
//
// 		var details = new InvalidRequestErrorDetails {
// 			Violations = { violations }
// 		};
//
// 		string message;
// 		if (validationResult.Errors.Count == 1) {
// 			var failure = validationResult.Errors[0];
// 			message = $"The argument '{failure.PropertyName}' is invalid: {failure.ErrorMessage}";
// 		} else {
// 			message = validationResult.Errors.Aggregate(
// 				new System.Text.StringBuilder($"The following arguments are invalid:{Environment.NewLine}"),
// 				(sb, failure) => sb.AppendLine($"- {failure.PropertyName}: {failure.ErrorMessage}")
// 			).ToString();
// 		}
//
// 		return RpcExceptions.FromError(CommonError.InvalidRequest, message, details);
// 	}
//
// 	/// <summary>
// 	/// Creates an RPC exception for requests with invalid arguments from individual validation failures.
// 	/// This method is an overload that accepts individual validation failures and creates a ValidationResult internally.
// 	/// </summary>
// 	/// <param name="failures">
// 	/// An array of validation failures representing invalid request arguments.
// 	/// Each failure should contain a field name and error description.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>,
// 	/// including <see cref="InvalidRequestErrorDetails"/> details with field violations.
// 	/// </returns>
// 	public static RpcException InvalidRequest(params ValidationFailure[] failures) =>
// 		InvalidRequest(new ValidationResult(failures));
//
// 	/// <summary>
// 	/// Creates an RPC exception for a single invalid argument.
// 	/// This method is a convenience overload for reporting a single field validation error.
// 	/// </summary>
// 	/// <param name="field">
// 	/// The name of the invalid field or argument.
// 	/// This should correspond to the request parameter or property name.
// 	/// It can also be a request header or other input identifier.
// 	/// </param>
// 	/// <param name="description">
// 	/// A description of why the field is invalid.
// 	/// This should explain what was wrong with the provided value.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.InvalidArgument"/>,
// 	/// including <see cref="InvalidRequestErrorDetails"/> details with a single field violation.
// 	/// </returns>
// 	public static RpcException InvalidRequest(string field, string description) =>
// 		InvalidRequest(new ValidationFailure(field, description));
//
// 	/// <summary>
// 	/// Creates an RPC exception for operations that exceed their deadline or timeout.
// 	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.DeadlineExceeded"/>
// 	/// when operations take longer than expected. The exception includes retry information to guide client behavior.
// 	/// </summary>
// 	/// <param name="message">
// 	/// A detailed message describing the timeout condition.
// 	/// This should explain what operation timed out and provide context about the timeout.
// 	/// </param>
// 	/// <param name="retryAfter">
// 	/// An optional suggested retry delay. If not provided, defaults to 3 seconds.
// 	/// This indicates how long clients should wait before retrying the operation.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.DeadlineExceeded"/>,
// 	/// including <see cref="RetryInfoErrorDetails"/> details with the suggested retry delay.
// 	/// The error message includes both the provided message and retry guidance.
// 	/// </returns>
// 	public static RpcException OperationTimeout(string message, TimeSpan? retryAfter = null) {
// 		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");
//
// 		retryAfter ??= TimeSpan.FromSeconds(3);
//
// 		message = $"Operation timed out: {message} "
// 		        + $"Please try again after {retryAfter.Value.Humanize()}.";
//
// 		var details = new RetryInfoErrorDetails { RetryDelayMs = (int)retryAfter.Value.TotalMilliseconds };
//
// 		return RpcExceptions.FromError(CommonError.OperationTimeout, message, details);
// 	}
//
// 	/// <summary>
// 	/// Creates an RPC exception indicating that the server is not yet ready to handle requests.
// 	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>
// 	/// when the server is in a startup or initialization state and cannot process requests.
// 	/// The exception includes retry information to guide client behavior.
// 	/// </summary>
// 	/// <param name="retryAfter">
// 	/// An optional suggested retry delay. If not provided, defaults to 3 seconds.
// 	/// This indicates how long clients should wait before retrying the request.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>,
// 	/// including <see cref="Google.Rpc.RetryInfo"/> details with the suggested retry delay.
// 	/// The error message explains that the server is not ready and includes retry guidance.
// 	/// </returns>
// 	public static RpcException ServerNotReady(TimeSpan? retryAfter = null) {
// 		retryAfter ??= TimeSpan.FromSeconds(3);
//
// 		var message = $"The server is not yet ready to handle requests. "
// 		            + $"Please try again after {retryAfter.Value.Humanize()}.";
//
// 		var details = new RetryInfoErrorDetails { RetryDelayMs = (int)retryAfter.Value.TotalMilliseconds };
//
// 		return RpcExceptions.FromError(CommonError.ServerNotReady, message, details);
// 	}
//
// 	/// <summary>
// 	/// Creates an RPC exception indicating that the server is currently overloaded.
// 	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>
// 	/// when the server cannot handle additional requests due to high load or resource constraints.
// 	/// The exception includes retry information to guide client behavior.
// 	/// </summary>
// 	/// <param name="retryAfter">
// 	/// An optional suggested retry delay. If not provided, defaults to 5 seconds.
// 	/// This indicates how long clients should wait before retrying the request.
// 	/// The default is longer than other unavailable conditions due to the need for load to decrease.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.Unavailable"/>,
// 	/// including <see cref="RetryInfoErrorDetails"/> details with the suggested retry delay.
// 	/// The error message explains that the server is overloaded and includes retry guidance.
// 	/// </returns>
// 	public static RpcException ServerOverloaded(TimeSpan? retryAfter = null) {
// 		retryAfter ??= TimeSpan.FromSeconds(5);
//
// 		var message = "The server is currently overloaded and cannot handle the request. "
// 		            + $"Please try again after {retryAfter.Value.Humanize()}.";
//
// 		var details = new RetryInfoErrorDetails { RetryDelayMs = (int)retryAfter.Value.TotalMilliseconds };
//
// 		return RpcExceptions.FromError(CommonError.ServerOverloaded, message, details);
// 	}
//
// 	/// <summary>
// 	/// Creates an RPC exception indicating that the current node is not the leader in a clustered environment.
// 	/// This method is used to create an <see cref="RpcException"/> with status code <see cref="StatusCode.FailedPrecondition"/>
// 	/// when write operations are attempted on a non-leader node in a KurrentDB cluster.
// 	/// The exception includes information about the current leader node and optional retry guidance.
// 	/// </summary>
// 	/// <param name="leaderNodeId">
// 	/// The unique identifier of the leader node in the cluster.
// 	/// </param>
// 	/// <param name="leaderEndpoint">
// 	/// The network endpoint of the leader node.
// 	/// </param>
// 	/// <param name="retryAfter">
// 	/// An optional suggested retry delay. If provided, includes retry information in the error details.
// 	/// This is useful when leadership may be changing and clients should wait before retrying.
// 	/// </param>
// 	/// <returns>
// 	/// An <see cref="RpcException"/> with status code <see cref="StatusCode.FailedPrecondition"/>,
// 	/// including <see cref="NotLeaderNodeErrorDetails"/> details with leader node information
// 	/// and optional <see cref="RetryInfoErrorDetails"/> if a retry delay is specified.
// 	/// </returns>
// 	public static RpcException NotLeaderNode(Guid leaderNodeId, DnsEndPoint leaderEndpoint, TimeSpan? retryAfter = null) {
// 		Debug.Assert(leaderNodeId != Guid.Empty, "The leader node ID must not be empty!");
// 		Debug.Assert(!string.IsNullOrWhiteSpace(leaderEndpoint.Host), "The leader endpoint host must not be empty!");
// 		Debug.Assert(leaderEndpoint.Port > 0, "The leader endpoint port must be positive!");
//
// 		var message = "The server is not the leader node and cannot handle the request. "
// 		            + "Please retry your request against the leader node directly at "
// 		            + $"{leaderEndpoint.Host}:{leaderEndpoint.Port}";
//
// 		var notLeaderNode = new NotLeaderNodeErrorDetails {
// 			NodeId = leaderNodeId.ToString(),
// 			Host   = leaderEndpoint.Host,
// 			Port   = leaderEndpoint.Port,
// 		};
//
// 		IMessage[] details = retryAfter is not null
// 			? [notLeaderNode, new RetryInfoErrorDetails { RetryDelayMs = (int)retryAfter.Value.TotalMilliseconds }]
// 			: [notLeaderNode];
//
// 		return RpcExceptions.FromError(CommonError.NotLeaderNode, message, details);
// 	}
//
// 	/// <summary>
// 	/// Indicates that an internal server error has occurred.
// 	/// This method is used to create an <see cref="RpcException"/> with a standardized
// 	/// message format for internal server errors, including a prompt to contact support.
// 	/// The provided message is appended to the standard message to give more context about the error.
// 	/// </summary>
// 	/// <param name="message">
// 	/// A detailed message describing the internal server error.
// 	/// This message should provide additional context about the error that occurred.
// 	/// It is recommended to include relevant information that can help in diagnosing the issue.
// 	/// </param>
// 	public static RpcException InternalServerError(string message) {
// 		Debug.Assert(!string.IsNullOrWhiteSpace(message), "The message must not be empty!");
//
// 		var statusMessage = $"An internal server error occurred. "
// 		                  + $"Please contact support if the problem persists.{Environment.NewLine}"
// 		                  + $"{message}";
//
// 		return RpcExceptions.FromError(CommonError.ServerMalfunction, statusMessage);
// 	}
//
// 	/// <summary>
// 	/// Indicates that an internal server error has occurred due to an exception.
// 	/// This method is used to create an <see cref="RpcException"/> with a standardized
// 	/// message format for internal server errors, including a prompt to contact support.
// 	/// The message from the provided exception is appended to the standard message to give more context about the error.
// 	/// Additionally, the exception's details are converted to a <see cref="Google.Rpc.DebugInfo"/> message
// 	/// and included in the error details to aid in debugging.
// 	/// </summary>
// 	/// <param name="exception">
// 	/// The exception that caused the internal server error.
// 	/// This exception's message will be included in the error message to provide context about the error.
// 	/// The exception's details will also be converted to a <see cref="Google.Rpc.DebugInfo"/>
// 	/// and included in the error details to help diagnose the issue.
// 	/// It is recommended to pass the original exception that triggered the error.
// 	/// </param>
// 	/// <param name="message">
// 	/// An optional detailed message describing the internal server error.
// 	/// This message should provide additional context about the error that occurred.
// 	/// If provided, it will be appended to the standard message replacing the exception's message.
// 	/// It is recommended to include relevant information that can help in diagnosing the issue.
// 	/// </param>
// 	public static RpcException InternalServerError(Exception exception, string? message = null) {
// 		Debug.Assert(exception is not RpcException, "The provided exception is already an RpcException. Don't wrap it again!");
//
// 		message = $"An internal server error occurred. "
// 		        + $"Please contact support if the problem persists.{Environment.NewLine}"
// 		        + $"{message ?? exception.Message}";
//
// 		return RpcExceptions.FromError(CommonError.ServerMalfunction, message, exception.ToRpcDebugInfo());
// 	}
// }
