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
	public static async Task<WriteEventsResult> WriteEvents(
        this IPublisher publisher,
        string stream, Event[] events,
        long expectedRevision = ExpectedVersion.Any,
        CancellationToken cancellationToken = default
    ) {
        var cid = Guid.NewGuid();

        var operation = new WriteEventsOperation(stream, expectedRevision);

        try {
            var command = ClientMessage.WriteEvents.ForSingleStream(
                internalCorrId: cid,
                correlationId: cid,
                envelope: operation,
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

        return await operation.WaitForReply;
    }
}

class WriteEventsOperation(string stream, long expectedRevision) : IEnvelope {
	TaskCompletionSource<WriteEventsResult> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public void ReplyWith<T>(T message) where T : Message {
		if (message is ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } success)
			Operation.TrySetResult(MapToResult(success));
		else
			Operation.TrySetException(MapToError(message, stream, expectedRevision));

		return;

		static WriteEventsResult MapToResult(ClientMessage.WriteEventsCompleted completed) {
			var position       = Position.FromInt64(completed.CommitPosition, completed.PreparePosition);
			var streamRevision = StreamRevision.FromInt64(completed.LastEventNumbers.Single);
			return new(position, streamRevision);
		}

		static ReadResponseException MapToError(Message message, string stream, long expectedRevision) {
			return message switch {
				ClientMessage.WriteEventsCompleted completed => completed.Result switch {
					OperationResult.PrepareTimeout       => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.CommitTimeout        => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.ForwardTimeout       => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.StreamDeleted        => new ReadResponseException.StreamDeleted(stream),
					OperationResult.AccessDenied         => new ReadResponseException.AccessDenied(),
					OperationResult.WrongExpectedVersion => new ReadResponseException.WrongExpectedRevision(stream, expectedRevision, completed.FailureCurrentVersions.Single),
					_                                    => ReadResponseException.UnknownError.Create(completed.Result)
				},
				ClientMessage.NotHandled notHandled => notHandled.MapToException(),
				not null                            => new ReadResponseException.UnknownMessage(message.GetType(), typeof(ClientMessage.WriteEventsCompleted)),
				_                                   => throw new ArgumentOutOfRangeException(nameof(message), message, null)
			};
		}
	}

	public Task<WriteEventsResult> WaitForReply => Operation.Task;
}
