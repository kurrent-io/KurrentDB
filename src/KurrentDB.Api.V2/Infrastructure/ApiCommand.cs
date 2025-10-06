// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using Humanizer;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using static System.StringComparison;

namespace KurrentDB.Api;

abstract class ApiCommand<TCommand> where TCommand : ApiCommand<TCommand> {
    protected ApiCommand(string? friendlyName = null) {
        FriendlyName = friendlyName ?? GetType().Name
            .Replace("command", "", OrdinalIgnoreCase)
            .Replace("request", "", OrdinalIgnoreCase)
            .Replace("callback", "", OrdinalIgnoreCase)
            .Replace("envelope", "", OrdinalIgnoreCase)
            .Humanize();
    }

    protected string FriendlyName { get; }

    protected IPublisher   Publisher { get; private set; } = null!;
    protected TimeProvider Time      { get; private set; } = TimeProvider.System;

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

abstract class ApiCommand<TCommand, TResult>(string? friendlyName = null) : ApiCommand<TCommand>(friendlyName) where TCommand : ApiCommand<TCommand, TResult> where TResult : IMessage {
    protected abstract Message BuildMessage(IEnvelope callback, ServerCallContext context);

    protected abstract bool SuccessPredicate(Message message);

    protected abstract TResult MapToResult(Message message);

    protected abstract RpcException? MapToError(Message message);

    protected virtual ValueTask OnError(Exception exception, ServerCallContext context) =>
        ValueTask.CompletedTask;

    protected virtual ValueTask OnSuccess(TResult result, ServerCallContext context) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Executes the command asynchronously.
    /// <remarks>
    /// It constructs a DelegateCallback with the provided predicates and mappers, publishes the message,
    /// and awaits the result, handling success and error cases appropriately.
    /// </remarks>
    /// </summary>
    public async Task<TResult> Execute(ServerCallContext context) {
        Debug.Assert(Publisher is not null, "Publisher must be set before executing the command.");

        WithTime(context.GetHttpContext().RequestServices.GetRequiredService<TimeProvider>());

        var self = (TCommand)this;

        var callback = new DelegateCallback<TCommand, TResult>(
            context, self, FriendlyName,
            static (msg, cmd, _) => cmd.SuccessPredicate(msg),
            static (msg, cmd, _) => cmd.MapToResult(msg),
            static (msg, cmd, _) => cmd.MapToError(msg)
        );

        try {
            var message = BuildMessage(callback, context);
            Publisher.Publish(message);
            var result = await callback.WaitForReply;
            await OnSuccess(result, context);
            return result;
        }
        catch (AggregateException aex) {
            var ex = aex.InnerException ?? aex.Flatten();
            await OnError(ex, context);
            throw ex;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            await OnError(ex, context);
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
