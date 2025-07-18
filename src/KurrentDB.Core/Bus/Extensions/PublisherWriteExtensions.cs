// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.Core;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

[PublicAPI]
public static class PublisherWriteExtensions {
    static void WriteEvents(
        this IPublisher publisher,
        string stream, Event[] events, long expectedRevision,
        TaskCompletionSource<WriteEventsResult> operation,
        CancellationToken cancellationToken = default
    ) {
        var cid = Guid.NewGuid();

        try {
            var command = ClientMessage.WriteEvents.ForSingleStream(
                internalCorrId: cid,
                correlationId: cid,
                envelope: new CallbackEnvelope<(TaskCompletionSource<WriteEventsResult>, string, long)>((operation, stream, expectedRevision), OnResult),
                requireLeader: false,
                eventStreamId: stream,
                expectedVersion: expectedRevision,
                events: events,
                user: SystemAccounts.System,
                cancellationToken: cancellationToken
            );

            publisher.Publish(command);
        } catch (Exception ex) {
            throw new($"{nameof(WriteEvents)}: Unable to execute request!", ex);
        }

        static void OnResult((TaskCompletionSource<WriteEventsResult> Operation, string Stream, long ExpectedRevision) arg, Message message) {
            if (message is ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } completed) {
                var position       = new Position((ulong)completed.CommitPosition, (ulong)completed.PreparePosition);
                var streamRevision = StreamRevision.FromInt64(completed.LastEventNumbers.Single);
                arg.Operation.TrySetResult(new(position, streamRevision));
            } else {
                arg.Operation.TrySetException(MapToError(arg.Stream, arg.ExpectedRevision, message));
            }
        }

        static ReadResponseException MapToError(string stream, long expectedRevision, Message message) {
            return message switch {
                ClientMessage.WriteEventsCompleted completed => completed.Result switch {
                    OperationResult.PrepareTimeout       => new ReadResponseException.Timeout($"{completed.Result}"),
                    OperationResult.CommitTimeout        => new ReadResponseException.Timeout($"{completed.Result}"),
                    OperationResult.ForwardTimeout       => new ReadResponseException.Timeout($"{completed.Result}"),
                    OperationResult.StreamDeleted        => new ReadResponseException.StreamDeleted(stream),
                    OperationResult.AccessDenied         => new ReadResponseException.AccessDenied(),
                    OperationResult.WrongExpectedVersion => new ReadResponseException.WrongExpectedRevision(stream, expectedRevision, completed.FailureCurrentVersions.Single),
                    _ => ReadResponseException.UnknownError.Create(completed.Result)
                },
                ClientMessage.NotHandled notHandled => notHandled.MapToException(),
                not null => new ReadResponseException.UnknownMessage(message.GetType(), typeof(ClientMessage.WriteEventsCompleted)),
                _ => throw new ArgumentOutOfRangeException(nameof(message), message, null)
            };
        }
    }

    public static Task<WriteEventsResult> WriteEvents(
        this IPublisher publisher,
        string stream, Event[] events, long expectedRevision = ExpectedVersion.Any,
        CancellationToken cancellationToken = default
    ) {
        try {
            var operation = new TaskCompletionSource<WriteEventsResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            publisher.WriteEvents(
                stream, events, expectedRevision,
                operation,
                cancellationToken
            );

            return operation.Task;
        } catch (Exception ex) {
            return Task.FromException<WriteEventsResult>(ex);
        }
    }
}
