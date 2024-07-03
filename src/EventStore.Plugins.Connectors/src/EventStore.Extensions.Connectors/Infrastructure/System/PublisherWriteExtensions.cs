// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;

namespace EventStore.Core;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

// Revision/Version
// NoStream = -1;
// Any = -2;
// StreamExists = -4;

[PublicAPI]
public static class PublisherWriteExtensions {
    static Task WriteEvents(
        this IPublisher publisher, string stream, Event[] events, long expectedRevision,
        Func<(Position? Position, StreamRevision? StreamRevision, Exception? Exception), Task> onResult,
        CancellationToken cancellationToken = default
    ) {
        var cid = Guid.NewGuid();

        try {
            var command = new ClientMessage.WriteEvents(
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
                var streamRevision = StreamRevision.FromInt64(completed.LastEventNumber);
                await onResult((position, streamRevision, null));
            } else {
                await onResult((null, null, MapToError(message)));
            }
        }

        ReadResponseException MapToError(Message message) {
            return message switch {
                ClientMessage.WriteEventsCompleted completed => completed.Result switch {
                    OperationResult.PrepareTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
                    OperationResult.CommitTimeout  => new ReadResponseException.Timeout($"{completed.Result}"),
                    OperationResult.ForwardTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
                    OperationResult.StreamDeleted  => new ReadResponseException.StreamDeleted(stream),
                    OperationResult.AccessDenied   => new ReadResponseException.AccessDenied(),
                    OperationResult.WrongExpectedVersion => new ReadResponseException.WrongExpectedRevision(
                        stream,
                        expectedRevision,
                        completed.CurrentVersion
                    ),
                    _ => ReadResponseException.UnknownError.Create(completed.Result)
                },
                ClientMessage.NotHandled notHandled => notHandled.MapToException(),
                not null => new ReadResponseException.UnknownMessage(
                    message.GetType(),
                    typeof(ClientMessage.WriteEventsCompleted)
                )
            };
        }
    }

    public static async Task<WriteEventsResult> WriteEvents(
        this IPublisher publisher, string stream, Event[] events, long expectedRevision = ExpectedVersion.Any,
        CancellationToken cancellationToken = default
    ) {
        var operation = new TaskCompletionSource<WriteEventsResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        await publisher.WriteEvents(
            stream,
            events,
            expectedRevision,
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