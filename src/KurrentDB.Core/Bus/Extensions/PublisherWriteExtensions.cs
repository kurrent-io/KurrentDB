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
    static Task WriteEvents(
        this IPublisher publisher,
        string stream, Event[] events, long expectedRevision,
        Func<(Position? Position, StreamRevision? StreamRevision, Exception? Exception), Task> onResult,
        CancellationToken cancellationToken = default
    ) {
        var cid = Guid.NewGuid();

        try {
            var command = ClientMessage.WriteEvents.ForSingleStream(
                internalCorrId: cid,
                correlationId: cid,
                envelope: AsyncCallbackEnvelope.Create(OnResult),
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

        return Task.CompletedTask;

        async Task OnResult(Message message) {
            if (message is ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } completed) {
                var position       = new Position((ulong)completed.CommitPosition, (ulong)completed.PreparePosition);
                var streamRevision = StreamRevision.FromInt64(completed.LastEventNumbers.Single);
                await onResult((position, streamRevision, null)).ConfigureAwait(false);
            } else {
                await onResult((null, null, MapToError(message))).ConfigureAwait(false);
            }
        }

        ReadResponseException MapToError(Message message) {
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

    public static async Task<WriteEventsResult> WriteEvents(
        this IPublisher publisher,
        string stream, Event[] events, long expectedRevision = ExpectedVersion.Any,
        CancellationToken cancellationToken = default
    ) {
        var operation = new TaskCompletionSource<WriteEventsResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        await publisher.WriteEvents(
            stream, events, expectedRevision,
            onResult: response => {
                if (response.Exception is null)
                    operation.TrySetResult(new(response.Position!.Value, response.StreamRevision!.Value));
                else
                    operation.TrySetException(response.Exception!);

                return Task.CompletedTask;
            },
            cancellationToken
        );

        return await operation.Task;
    }
}
