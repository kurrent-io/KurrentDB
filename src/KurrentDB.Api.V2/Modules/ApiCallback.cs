// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524, CS8509

using System.Collections.Concurrent;
using System.Reflection;
using DotNext;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Api;

abstract class ApiCallback<TState, TResponse, TError>(in TState state, string operationScope) : IEnvelope where TError : RpcException {
    TState State { get; } = state;

    TaskCompletionSource<TResponse> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReplyWith<T>(T message) where T : Message {
        try {
            // check for pre-success errors
            if (message.TryGetOperationResult(out var operationResult) && operationResult.IsOneOf([OperationResult.AccessDenied, OperationResult.ForwardTimeout])) {
                Operation.TrySetException(operationResult switch {
                    // AccessDenied is more of an edge case since we check permissions before starting the operation
                    OperationResult.AccessDenied   => ApiErrors.AccessDenied(operationScope),
                    OperationResult.ForwardTimeout => ApiErrors.OperationTimeout($"The {operationScope} timed out while waiting to be forwarded to the leader"),
                });

                return;
            }

            // check for success
            if (SuccessPredicate(message, State)) {
                try {
                    Operation.TrySetResult(MapToApiResponse(message, State));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    Operation.TrySetException(ApiErrors.InternalServerError(ex, $"Failed to map response for operation {operationScope}: {ex.Message}"));
                }

                return;
            }

            // special handling for NotHandled messages to avoid unnecessary exception wrapping
            if (message is ClientMessage.NotHandled notHandled) {
                Operation.TrySetException(notHandled.Reason switch {
                    ClientMessage.NotHandled.Types.NotHandledReason.NotReady   => ApiErrors.ServerNotReady(),
                    ClientMessage.NotHandled.Types.NotHandledReason.TooBusy    => ApiErrors.ServerOverloaded(),
                    ClientMessage.NotHandled.Types.NotHandledReason.NotLeader  => ApiErrors.InternalServerError("Server is not the leader"),
                    ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => ApiErrors.InternalServerError("Server is in read-only mode"),
                });

                return;
            }

            // otherwise, treat as a normal post-success error
            try {
                Operation.TrySetException(MapToApiError(message, State));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                Operation.TrySetException(
                    ApiErrors.InternalServerError(ex, $"Failed to map error for operation {operationScope}: {ex.Message}"));
            }
        }
        catch (OperationCanceledException ex) {
            Operation.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex) {
            Operation.TrySetException(
                ApiErrors.InternalServerError(ex, $"Failed to process the callback message for operation {operationScope}: {ex.Message}"));
        }
    }

    public Task<TResponse> WaitForReply => Operation.Task;

    protected abstract bool      SuccessPredicate(Message message, TState state);
    protected abstract TResponse MapToApiResponse(Message message, TState state);
    protected abstract TError    MapToApiError(Message message, TState state);
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
