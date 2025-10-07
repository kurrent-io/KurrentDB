// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api;

abstract class ApiCommand<TCommand> where TCommand : ApiCommand<TCommand> {
    /// <summary>
    /// The message bus publisher to be used by the command.
    /// This must be set before executing the command.
    /// It is typically provided via dependency injection or a factory method.
    /// Attempting to execute the command without setting the publisher will result in an assertion failure.
    /// </summary>
    protected IPublisher Publisher { get; private set; } = null!;

    /// <summary>
    /// The time provider to be used by the command.
    /// Default is TimeProvider.System.
    /// This can be overridden for testing or custom time sources.
    /// </summary>
    protected TimeProvider Time { get; private set; } = TimeProvider.System;

    /// <summary>
    /// Sets the message bus publisher to be used by the command.
    /// </summary>
    protected internal TCommand WithPublisher(IPublisher publisher) {
        Publisher = publisher;
        return (TCommand)this;
    }

    /// <summary>
    /// Sets the time provider to be used by the command.
    /// Default is TimeProvider.System.
    /// </summary>
    public TCommand WithTime(TimeProvider time) {
        Time = time;
        return (TCommand)this;
    }
}

abstract class ApiCommand<TCommand, TResult> : ApiCommand<TCommand> where TCommand : ApiCommand<TCommand, TResult> where TResult : IMessage {
    /// <summary>
    /// The friendly name of the command being executed.
    /// This is used for logging and error messages to provide context about the operation.
    /// It should describe the action being performed, e.g., "Read Event", "Write Events", etc.
    /// <remarks>
    /// It is automatically derived from the server call context if not provided.
    /// </remarks>
    /// </summary>
    protected string FriendlyName { get; private set; } = null!;

    /// <summary>
    /// Builds the message to be published to the message bus.
    /// </summary>
    protected abstract Message BuildMessage(IEnvelope callback, ServerCallContext context);

    /// <summary>
    /// Predicate to determine if the incoming message indicates a successful operation.
    /// This is used to decide whether to map the message to a successful result or an error.
    /// The implementation should inspect the message and return true if it represents success, false otherwise.
    /// </summary>
    protected abstract bool SuccessPredicate(Message message);

    /// <summary>
    /// Maps the incoming message to a successful API result.
    /// This method is called when the SuccessPredicate returns true.
    /// The implementation should extract the relevant data from the message and construct the TResult object.
    /// </summary>
    protected abstract TResult MapToResult(Message message);

    /// <summary>
    /// Maps the incoming message to an RPC exception representing an error.
    /// This method is called when the SuccessPredicate returns false.
    /// Returning null indicates that the message type was unexpected
    /// and a generic internal server error will be generated instead.
    /// </summary>
    protected abstract RpcException? MapToError(Message message);

    /// <summary>
    /// Executes the command asynchronously.
    /// <remarks>
    /// It constructs a DelegateCallback with the provided predicates and mappers, publishes the message,
    /// and awaits the result, handling success and error cases appropriately.
    /// </remarks>
    /// </summary>
    public async ValueTask<TResult> Execute(ServerCallContext context) {
        Debug.Assert(Publisher is not null, "Publisher must be set before executing the command.");

        WithTime(context.GetTimeProvider());

        FriendlyName = context.GetFriendlyOperationName();

        var self = (TCommand)this;

        var callback = new DelegateCallback<TCommand, TResult>(
            context, self, FriendlyName,
            static (msg, cmd, _) => cmd.SuccessPredicate(msg),
            static (msg, cmd, _) => cmd.MapToResult(msg),
            static (msg, cmd, _) => cmd.MapToError(msg));

        try {
            var message = BuildMessage(callback, context);
            Publisher.Publish(message);
            var result = await callback.WaitForReply;
            return result;
        }
        catch (AggregateException aex) {
            var ex = aex.InnerException ?? aex.Flatten();
            throw ex;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw;
        }
    }
}

static class PublisherExtensions {
    /// <summary>
    /// Creates a new api command of the specified type, associating it with the given publisher.
    /// </summary>
    public static TCommand NewCommand<TCommand>(this IPublisher publisher) where TCommand : ApiCommand<TCommand>, new() =>
        new TCommand().WithPublisher(publisher);
}
