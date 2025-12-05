// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
// ReSharper disable ArrangeTypeModifiers
// ReSharper disable ArrangeTypeMemberModifiers

#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.Core.ClientPublisher;

using WriteEventsResult = (Position Position, StreamRevision[] StreamRevisions);

[PublicAPI]
public static class PublisherWriteExtensions {
	public static async Task<(Position Position, StreamRevision StreamRevision)> WriteEvents(
		this IPublisher publisher,
		string stream,
		Event[] events,
		long expectedRevision = ExpectedVersion.Any,
		CancellationToken cancellationToken = default
	) {
		var result = await publisher.WriteEvents([stream], [expectedRevision], events, [0], cancellationToken);
		return (result.Position, result.StreamRevisions[0]);
	}

	public static async Task<WriteEventsResult> WriteEvents(
		this IPublisher publisher,
		string[] eventStreamIds,
		long[] expectedVersions,
		Event[] events,
		int[] eventStreamIndexes,
		CancellationToken cancellationToken = default
	) {
		var correlationId = Guid.NewGuid();

		var envelope = new WriteEventsOperation(eventStreamIds, expectedVersions);

		try {
			var command = new ClientMessage.WriteEvents(
				internalCorrId: correlationId,
				correlationId,
				envelope,
				requireLeader: true,
				eventStreamIds,
				expectedVersions,
				events: events,
				eventStreamIndexes,
				user: SystemAccounts.System,
				cancellationToken: cancellationToken
			);

			publisher.Publish(command);
		} catch (Exception ex) {
			throw new($"{nameof(WriteEvents)}: Unable to execute request!", ex);
		}

		return await envelope.WaitForReply;
	}
}

class WriteEventsOperation(string[] streams, long[] expectedVersions) : IEnvelope {
	TaskCompletionSource<WriteEventsResult> Operation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public void ReplyWith<T>(T message) where T : Message {
		if (message is ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } success)
			Operation.TrySetResult(MapToResult(success));
		else
			Operation.TrySetException(MapToError(message, streams, expectedVersions));

		return;

		static WriteEventsResult MapToResult(ClientMessage.WriteEventsCompleted completed) {
			Debug.Assert(completed.CommitPosition >= 0);
			Debug.Assert(completed.PreparePosition >= 0);
			var position = Position.FromInt64(completed.CommitPosition, completed.PreparePosition);
			var streamRevisions = new StreamRevision[completed.LastEventNumbers.Length];
			for (int i = 0; i < completed.LastEventNumbers.Length; i++) {
				streamRevisions[i] = StreamRevision.FromInt64(completed.LastEventNumbers.Span[i]);
			}
			return new WriteEventsResult(position, streamRevisions);
		}

		static ReadResponseException MapToError(Message message, string[] streams, long[] expectedVersions) {
			return message switch {
				ClientMessage.WriteEventsCompleted completed => completed.Result switch {
					OperationResult.PrepareTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.CommitTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.ForwardTimeout => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.AccessDenied => new ReadResponseException.AccessDenied(),
					OperationResult.StreamDeleted => MapStreamDeletedError(completed, streams),
					OperationResult.WrongExpectedVersion => MapWrongExpectedVersionError(completed, streams, expectedVersions),
					_ => ReadResponseException.UnknownError.Create(completed.Result)
				},
				ClientMessage.NotHandled notHandled => notHandled.MapToException(),
				not null => new ReadResponseException.UnknownMessage(message.GetType(), typeof(ClientMessage.WriteEventsCompleted)),
				_ => throw new ArgumentOutOfRangeException(nameof(message), message, null)
			};

			static ReadResponseException MapStreamDeletedError(ClientMessage.WriteEventsCompleted completed, string[] streams) {
				var streamIndex = completed.FailureStreamIndexes.Span[0];
				return new ReadResponseException.StreamDeleted(streams[streamIndex]);
			}

			static ReadResponseException MapWrongExpectedVersionError(ClientMessage.WriteEventsCompleted completed, string[] streams, long[] expectedVersions) {
				var streamIndex = completed.FailureStreamIndexes.Span[0];
				var currentVersion = completed.FailureCurrentVersions.Span[0];
				return new ReadResponseException.WrongExpectedRevision(streams[streamIndex], expectedVersions[streamIndex], currentVersion);
			}
		}
	}

	public Task<WriteEventsResult> WaitForReply => Operation.Task;
}
