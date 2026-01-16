// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.Core.ClientPublisher;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);
using WriteEventsMultiResult = (Position Position, LowAllocReadOnlyMemory<StreamRevision> StreamRevisions);

[PublicAPI]
public static class PublisherWriteExtensions {
	public static async Task<WriteEventsResult> WriteEvents(
		this IPublisher publisher,
		string stream,
		Event[] events,
		long expectedRevision = ExpectedVersion.Any,
		CancellationToken cancellationToken = default
	) {
		var result = await publisher.WriteEvents(
			streams: new(stream),
			expectedRevisions: new(expectedRevision),
			events: new(events),
			eventStreamIndexes: default,
			cancellationToken: cancellationToken);

		return new(result.Position, result.StreamRevisions.Single);
	}

	public static async Task<WriteEventsMultiResult> WriteEvents(
		this IPublisher publisher,
		LowAllocReadOnlyMemory<string> streams,
		LowAllocReadOnlyMemory<long> expectedRevisions,
		LowAllocReadOnlyMemory<Event> events,
		LowAllocReadOnlyMemory<int> eventStreamIndexes,
		CancellationToken cancellationToken = default
	) {
		var cid = Guid.NewGuid();

		var operation = new WriteEventsOperation(streams, expectedRevisions);

		try {
			var command = new ClientMessage.WriteEvents(
				internalCorrId: cid,
				correlationId: cid,
				envelope: operation,
				requireLeader: false,
				eventStreamIds: streams,
				expectedVersions: expectedRevisions,
				events: events,
				eventStreamIndexes: eventStreamIndexes,
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

class WriteEventsOperation(LowAllocReadOnlyMemory<string> streams, LowAllocReadOnlyMemory<long> expectedRevisions) : IEnvelope {
	TaskCompletionSource<WriteEventsMultiResult> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public void ReplyWith<T>(T message) where T : Message {
		if (message is ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } success)
			Operation.TrySetResult(MapToResult(success));
		else
			Operation.TrySetException(MapToError(message, streams, expectedRevisions));

		return;

		static WriteEventsMultiResult MapToResult(ClientMessage.WriteEventsCompleted completed) {
			Debug.Assert(completed.CommitPosition >= 0);
			Debug.Assert(completed.PreparePosition >= 0);
			var position = Position.FromInt64(completed.CommitPosition, completed.PreparePosition);

			LowAllocReadOnlyMemory<StreamRevision> streamRevisions;
			var lens = completed.LastEventNumbers;
			if (lens.Length is 1) {
				streamRevisions = new(StreamRevision.FromInt64(lens.Single));
			} else {
				var rs = new StreamRevision[lens.Length];
				for (int i = 0; i < lens.Length; i++) {
					rs[i] = StreamRevision.FromInt64(lens.Span[i]);
				}
				streamRevisions = new(rs);
			}

			return new(position, streamRevisions);
		}

		static ReadResponseException MapToError(Message message, LowAllocReadOnlyMemory<string> streams, LowAllocReadOnlyMemory<long> expectedRevisions) {
			return message switch {
				ClientMessage.WriteEventsCompleted completed => completed.Result switch {
					OperationResult.PrepareTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.CommitTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.ForwardTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.StreamDeleted => new ReadResponseException.StreamDeleted(streams.Span[completed.FailureStreamIndexes.Span[0]]),
					OperationResult.AccessDenied => new ReadResponseException.AccessDenied(),
					OperationResult.WrongExpectedVersion => new ReadResponseException.WrongExpectedRevision(
						stream: streams.Span[completed.FailureStreamIndexes.Span[0]],
						expectedRevision: expectedRevisions.Span[completed.FailureStreamIndexes.Span[0]],
						actualRevision: completed.FailureCurrentVersions.Span[0]),
					_ => ReadResponseException.UnknownError.Create(completed.Result)
				},
				ClientMessage.NotHandled notHandled => notHandled.MapToException(),
				not null => new ReadResponseException.UnknownMessage(message.GetType(), typeof(ClientMessage.WriteEventsCompleted)),
				_ => throw new ArgumentOutOfRangeException(nameof(message), message, null)
			};
		}
	}

	public Task<WriteEventsMultiResult> WaitForReply => Operation.Task;
}
