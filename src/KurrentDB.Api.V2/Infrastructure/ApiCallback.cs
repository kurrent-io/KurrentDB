// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524, CS8509

using System.Collections.Concurrent;
using System.Reflection;
using DotNext;
using DotNext.Threading.Tasks;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.Authorization;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using static System.StringComparison;

namespace KurrentDB.Api;

/// <summary>
/// Base class for handling API callbacks from the internal message bus to gRPC responses.
/// <remarks>
/// This class manages the lifecycle of the callback, including success and error handling.
/// It uses generic delegates to allow for flexible mapping of messages to API responses and errors.
/// </remarks>
/// </summary>
/// <typeparam name="TState">
/// The type of the state object passed to the mapping functions.
/// </typeparam>
/// <typeparam name="TResponse">
/// The type of the successful API response.
/// </typeparam>
abstract class ApiCallbackBase<TState, TResponse> : IEnvelope {
    protected ApiCallbackBase(ServerCallContext context, in TState state, string? operationName = null) {
        CallContext = context;
        State       = state;

        OperationName = operationName ?? GetType().Name
            .Replace("callback", "", OrdinalIgnoreCase)
            .Replace("envelope", "", OrdinalIgnoreCase)
            .Replace("reply", "", OrdinalIgnoreCase)
            .Humanize();
    }

    ServerCallContext CallContext { get; }
    TState            State       { get; }

    TaskCompletionSource<TResponse> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The name of the operation being performed.
    /// This is used for logging and error messages to provide context about the operation.
    /// It should describe the action being performed, e.g., "ReadEvent", "WriteEvents", etc.
    /// <remarks>
    /// It is automatically derived from the class name if not provided,
    /// by removing common suffixes like "Callback", "Envelope", and "Reply".
    /// This ensures that the operation name is always meaningful and consistent.
    /// </remarks>
    /// </summary>
    protected string OperationName { get; }

    /// <summary>
    /// A task that completes when the operation is finished, either successfully or with an error.
    /// This allows the caller to await the result of the operation.
    /// </summary>
    public Task<TResponse> WaitForReply => Operation.Task;

    /// <summary>
    /// Handles the incoming message and completes the operation accordingly.
    /// This method checks for pre-success errors, determines if the message indicates success,
    /// and maps the message to either a successful response or an error.
    /// It uses the provided delegates to perform the necessary mappings.
    /// </summary>
    /// <param name="message">
    /// The incoming message from the internal message bus.
    /// This message is processed to determine the outcome of the operation.
    /// </param>
    /// <typeparam name="T">
    /// The specific type of the message being handled. Must be a subclass of <see cref="Message"/>.
    /// </typeparam>
    public void ReplyWith<T>(T message) where T : Message {
        try {
            // check for pre-success errors
            if (message.TryGetOperationResult(out var operationResult))
                switch (operationResult) {
                    case OperationResult.AccessDenied:
                        Operation.TrySetException(ApiErrors.AccessDenied(CallContext.Method, CallContext.GetHttpContext().User.Identity?.Name));
                        return;
                    case OperationResult.ForwardTimeout:
                        Operation.TrySetException(ApiErrors.OperationTimeout($"{OperationName} timed out while waiting to be forwarded to the leader"));
                        return;
                }

            // check for success
            if (SuccessPredicate(message, State, CallContext)) {
                try {
                    Operation.TrySetResult(MapToResponse(message, State, CallContext));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    Operation.TrySetException(ApiErrors.InternalServerError(ex, $"{OperationName} failed to map response: {ex.Message}"));
                }

                return;
            }

            // special handling for NotHandled messages to avoid unnecessary exception wrapping
            if (message is ClientMessage.NotHandled notHandled) {
                Operation.TrySetException(notHandled.Reason switch {
                    ClientMessage.NotHandled.Types.NotHandledReason.NotReady   => ApiErrors.ServerNotReady(),
                    ClientMessage.NotHandled.Types.NotHandledReason.TooBusy    => ApiErrors.ServerOverloaded(),
                    ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => ApiErrors.InternalServerError("Server is in read-only mode"),
                    ClientMessage.NotHandled.Types.NotHandledReason.NotLeader  => ApiErrors.NotLeaderNode(
                        CallContext.GetHttpContext().RequestServices.GetRequiredService<INodeSystemInfoProvider>()
                            .GetLeaderInfo(CallContext.CancellationToken).Wait()),
                });

                return;
            }

            // otherwise, treat as a normal post-success error
            try {
                var err = MapToError(message, State, CallContext)
                       ?? ApiErrors.InternalServerError($"{OperationName} failed with unexpected callback message: {message.GetType().FullName}");

                Operation.TrySetException(err);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                Operation.TrySetException(ApiErrors.InternalServerError(ex, $"{OperationName} failed to map error: {ex.Message}"));
            }
        }
        catch (OperationCanceledException ex) {
            Operation.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex) {
            Operation.TrySetException(ApiErrors.InternalServerError(ex, $"{OperationName} failed to process callback message: {ex.Message}"));
        }
    }

    /// <summary>
    /// Determines whether the incoming message indicates a successful operation.
    /// </summary>
    protected abstract bool SuccessPredicate(Message message, TState state, ServerCallContext context);

    /// <summary>
    /// Maps a successful message to the corresponding API response.
    /// </summary>
    protected abstract TResponse MapToResponse(Message message, TState state, ServerCallContext context);

    /// <summary>
    /// Maps an error message to the corresponding RPC exception.
    /// <remarks>
    /// Returning null indicates that the message type was unexpected
    /// and a generic internal server error will be generated instead.
    /// </remarks>
    /// </summary>
    protected abstract RpcException? MapToError(Message message, TState state, ServerCallContext context);
}

/// <summary>
/// Base class for handling API callbacks from the internal message bus to gRPC responses.
/// <remarks>
/// This class manages the lifecycle of the callback, including success and error handling.
/// It uses generic delegates to allow for flexible mapping of messages to API responses and errors.
/// </remarks>
/// </summary>
/// <typeparam name="TState">
/// The type of the state object passed to the mapping functions.
/// </typeparam>
/// <typeparam name="TResponse">
/// The type of the successful API response.
/// </typeparam>
abstract class ApiCallback<TState, TResponse> : IEnvelope {
    protected ApiCallback(Permission permission, INodeSystemInfoProvider node, in TState state, string? operationName = null) {
        Permission = permission;
        Node       = node;
        State      = state;

        OperationName = operationName ?? GetType().Name
            .Replace("callback", "", OrdinalIgnoreCase)
            .Replace("envelope", "", OrdinalIgnoreCase)
            .Replace("reply", "", OrdinalIgnoreCase)
            .Humanize();
    }

    INodeSystemInfoProvider Node  { get; }
    TState                  State { get; }

    TaskCompletionSource<TResponse> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The name of the operation being performed.
    /// This is used for logging and error messages to provide context about the operation.
    /// It should describe the action being performed, e.g., "ReadEvent", "WriteEvents", etc.
    /// <remarks>
    /// It is automatically derived from the class name if not provided,
    /// by removing common suffixes like "Callback", "Envelope", and "Reply".
    /// This ensures that the operation name is always meaningful and consistent.
    /// </remarks>
    /// </summary>
    protected string OperationName { get; }

    /// <summary>
    /// The authorization claim required to perform the operation.
    /// Used to enrich access denied errors with the specific claim that was required.
    /// This helps clients understand what permissions are needed to successfully complete the operation.
    /// </summary>
    protected Permission Permission { get; }

    /// <summary>
    /// A task that completes when the operation is finished, either successfully or with an error.
    /// This allows the caller to await the result of the operation.
    /// </summary>
    public Task<TResponse> WaitForReply => Operation.Task;

    /// <summary>
    /// Handles the incoming message and completes the operation accordingly.
    /// This method checks for pre-success errors, determines if the message indicates success,
    /// and maps the message to either a successful response or an error.
    /// It uses the provided delegates to perform the necessary mappings.
    /// </summary>
    /// <param name="message">
    /// The incoming message from the internal message bus.
    /// This message is processed to determine the outcome of the operation.
    /// </param>
    /// <typeparam name="T">
    /// The specific type of the message being handled. Must be a subclass of <see cref="Message"/>.
    /// </typeparam>
    public void ReplyWith<T>(T message) where T : Message {
        try {
            // check for pre-success errors
            if (message.TryGetOperationResult(out var operationResult)
             && operationResult.IsOneOf([OperationResult.AccessDenied, OperationResult.ForwardTimeout])) {
                Operation.TrySetException(operationResult switch {
                    // AccessDenied is more of an edge case since we check permissions before starting the operation
                    OperationResult.AccessDenied   => ApiErrors.AccessDenied(Permission),
                    OperationResult.ForwardTimeout => ApiErrors.OperationTimeout($"{OperationName} timed out while waiting to be forwarded to the leader")
                });

                return;
            }

            // check for success
            if (SuccessPredicate(message, State)) {
                try {
                    Operation.TrySetResult(MapToResponse(message, State));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    Operation.TrySetException(ApiErrors.InternalServerError(ex, $"{OperationName} failed to map response: {ex.Message}"));
                }

                return;
            }

            RpcException HandleNotLeader(Message message) {
                var leaderInfo = Node.GetLeaderInfo(CancellationToken.None).Wait();
                return ApiErrors.NotLeaderNode(leaderInfo);
            }

            // special handling for NotHandled messages to avoid unnecessary exception wrapping
            if (message is ClientMessage.NotHandled notHandled) {
                Operation.TrySetException(notHandled.Reason switch {
                    ClientMessage.NotHandled.Types.NotHandledReason.NotReady   => ApiErrors.ServerNotReady(),
                    ClientMessage.NotHandled.Types.NotHandledReason.TooBusy    => ApiErrors.ServerOverloaded(),
                    ClientMessage.NotHandled.Types.NotHandledReason.NotLeader  => HandleNotLeader(message),
                    ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => ApiErrors.InternalServerError("Server is in read-only mode")
                });

                return;
            }

            // otherwise, treat as a normal post-success error
            try {
                var err = MapToError(message, State) ?? ApiErrors.InternalServerError(
                    $"{OperationName} failed with unexpected callback message: {message.GetType().FullName}");

                Operation.TrySetException(err);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                Operation.TrySetException(ApiErrors.InternalServerError(ex, $"{OperationName} failed to map error: {ex.Message}"));
            }
        }
        catch (OperationCanceledException ex) {
            Operation.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex) {
            Operation.TrySetException(ApiErrors.InternalServerError(ex, $"{OperationName} failed to process callback message: {ex.Message}"));
        }
    }

    /// <summary>
    /// Determines whether the incoming message indicates a successful operation.
    /// </summary>
    protected abstract bool SuccessPredicate(Message message, TState state);

    /// <summary>
    /// Maps a successful message to the corresponding API response.
    /// </summary>
    protected abstract TResponse MapToResponse(Message message, TState state);

    /// <summary>
    /// Maps an error message to the corresponding RPC exception.
    /// <remarks>
    /// Returning null indicates that the message type was unexpected
    /// and a generic internal server error will be generated instead.
    /// </remarks>
    /// </summary>
    protected abstract RpcException? MapToError(Message message, TState state);
}

static class MessageExtensions {
    static ConcurrentDictionary<Type, Func<Message, OperationResult>> OperationResultFieldCache { get; } = new();

    /// <summary>
    /// Attempts to extract the OperationResult field from a Message, if it exists.
    /// <remarks>
    /// This allows us to generically check for pre-success errors without needing to know the specific message type.
    /// Note that this uses reflection and caching, so it should be reasonably efficient after the first lookup.
    /// The OperationResult field is optional and may not exist on all message types.
    /// </remarks>
    /// </summary>
    public static bool TryGetOperationResult(this Message message, out OperationResult result) {
        if (OperationResultFieldCache.GetOrAdd(message.GetType(), ValueFactory(message)) is { } getResult) {
            result = getResult(message);
            return true;
        }

        result = default;
        return false;

        static Func<Type, Func<Message, OperationResult>> ValueFactory(Message message) =>
            msgType => msgType.GetRuntimeFields().FirstOrDefault(f => f.FieldType == typeof(OperationResult)) is { } field
                ? msg => (OperationResult)field.GetValue(msg)!
                : null!;
    }
}

// sealed class DelegateCallback<TState, TResponse>(
//     Permission permission,
//     INodeSystemInfoProvider node,
//     in TState state,
//     string? operationName,
//     Func<Message, TState, bool> successPredicate,
//     Func<Message, TState, TResponse> responseMapper,
//     Func<Message, TState, RpcException?> errorMapper
// ) : ApiCallback<TState, TResponse>(permission, node, state, operationName) {
//     protected override bool          SuccessPredicate(Message message, TState state) => successPredicate(message, state);
//     protected override TResponse     MapToResponse(Message message, TState state)    => responseMapper(message, state);
//     protected override RpcException? MapToError(Message message, TState state)       => errorMapper(message, state);
// }

sealed class DelegateCallback<TState, TResponse>(
    ServerCallContext context,
    in TState state,
    string? operationName,
    Func<Message, TState, ServerCallContext, bool> successPredicate,
    Func<Message, TState, ServerCallContext, TResponse> responseMapper,
    Func<Message, TState, ServerCallContext, RpcException?> errorMapper
) : ApiCallbackBase<TState, TResponse>(context, state, operationName) {
    protected override bool          SuccessPredicate(Message msg, TState state, ServerCallContext ctx) => successPredicate(msg, state, ctx);
    protected override TResponse     MapToResponse(Message msg, TState state, ServerCallContext ctx)    => responseMapper(msg, state, ctx);
    protected override RpcException? MapToError(Message msg, TState state, ServerCallContext ctx)       => errorMapper(msg, state, ctx);
}
