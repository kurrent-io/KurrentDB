// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;

namespace EventStore.Core;

[PublicAPI]
public static class PublisherManagementExtensions {
	public static Task<(Position Position, StreamRevision Revision)> DeleteStream(this IPublisher publisher, string stream, long expectedRevision = -2, bool hardDelete = false, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		var operation = new TaskCompletionSource<(Position Position, StreamRevision StreamRevision)>(TaskCreationOptions.RunContinuationsAsynchronously);

		var cid = Guid.NewGuid();

		try {
			var command = new ClientMessage.DeleteStream(
				internalCorrId: cid,
				correlationId: cid,
				envelope: new CallbackEnvelope(Callback),
				requireLeader: false,
				eventStreamId: stream,
				expectedVersion: expectedRevision,
				hardDelete: hardDelete,
				user: SystemAccounts.System,
				cancellationToken: cancellationToken
			);

			publisher.Publish(command);
		}
		catch (Exception ex) {
			operation.TrySetException(new Exception($"{nameof(DeleteStream)}: Unable to execute request!", ex));
		}

		return operation.Task;

		void Callback(Message message) {
			if (message is ClientMessage.DeleteStreamCompleted { Result: OperationResult.Success } completed) {
				var position = new Position((ulong)completed.CommitPosition, (ulong)completed.PreparePosition);
				var streamRevision = StreamRevision.FromInt64(completed.CurrentVersion);

				operation.TrySetResult(new(position, streamRevision));
			}
			else
				operation.TrySetException(MapToError(message));
		}

		ReadResponseException MapToError(Message message) {
			return message switch {
				ClientMessage.DeleteStreamCompleted completed => completed.Result switch {
					OperationResult.PrepareTimeout       => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.CommitTimeout        => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.ForwardTimeout       => new ReadResponseException.Timeout($"{completed.Result}"),
					OperationResult.StreamDeleted        => new ReadResponseException.StreamDeleted(stream),
					OperationResult.WrongExpectedVersion => new ReadResponseException.WrongExpectedRevision(stream, expectedRevision, completed.CurrentVersion),
					OperationResult.AccessDenied         => new ReadResponseException.AccessDenied(),
					_                                    => ReadResponseException.UnknownError.Create(completed.Result)
				},
				ClientMessage.NotHandled notHandled => notHandled.MapToException(),
				not null                            => new ReadResponseException.UnknownMessage(message.GetType(), typeof(ClientMessage.DeleteStreamCompleted))
			};
		}
	}

	public static Task<(Position Position, StreamRevision Revision)> SoftDeleteStream(
		this IPublisher publisher, string stream, long expectedRevision = -2, CancellationToken cancellationToken = default) =>
		publisher.DeleteStream(stream, expectedRevision, false, cancellationToken);

    public static Task<(Position Position, StreamRevision Revision)> SoftDeleteStream(
        this IPublisher publisher, string stream, CancellationToken cancellationToken = default) =>
        publisher.DeleteStream(stream, -2, false, cancellationToken);

	public static Task<(Position Position, StreamRevision Revision)> HardDeleteStream(
		this IPublisher publisher, string stream, long expectedRevision = -2, CancellationToken cancellationToken = default) =>
		publisher.DeleteStream(stream, expectedRevision, true, cancellationToken);

	public static async Task<(StreamMetadata Metadata, long Revision)> SetStreamMetadata(this IPublisher publisher, string stream, StreamMetadata metadata, long expectedRevision = -2, CancellationToken cancellationToken = default) {
		var events = new[] {
			new Event(
				eventId: Guid.NewGuid(),
				eventType: SystemEventTypes.StreamMetadata,
				isJson: true,
				data: metadata.ToJsonBytes(),
				metadata: null
			)
		};

		var (position, streamRevision) = await publisher.WriteEvents(SystemStreams.MetastreamOf(stream), events, expectedRevision, cancellationToken);

		return (metadata, streamRevision.ToInt64());
	}

	public static async Task<(StreamMetadata Metadata, long Revision)> GetStreamMetadata(this IPublisher publisher, string stream, CancellationToken cancellationToken = default) {
		try {
			var lastEvent = await publisher.ReadStreamLastEvent(SystemStreams.MetastreamOf(stream), cancellationToken);

			return lastEvent is not null
				? (StreamMetadata.FromJsonBytes(lastEvent.Value.Event.Data), lastEvent.Value.Event.EventNumber)
				: (StreamMetadata.Empty, -2);
		}
		catch (ReadResponseException.StreamNotFound) {
			return (StreamMetadata.Empty, -2);
		}
	}

	public static async Task<bool> StreamExists(this IPublisher publisher, string stream, CancellationToken cancellationToken = default) {
		try {
			_ = await publisher.ReadStreamLastEvent(stream, cancellationToken);
			return true;
		}
		catch (ReadResponseException.StreamNotFound) {
			return false;
		}
	}

	public static async Task TruncateStream(this IPublisher publisher, string stream, long beforeRevision, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		var (metadata, revision) = await publisher.GetStreamMetadata(stream, cancellationToken);

		var newMetadata = new StreamMetadata(
			maxCount: metadata.MaxCount,
			truncateBefore: beforeRevision,
			maxAge: metadata.MaxAge,
			tempStream: metadata.TempStream,
			cacheControl: metadata.CacheControl,
			acl: metadata.Acl
		);

		await publisher.SetStreamMetadata(
			stream, newMetadata,
			revision,
			cancellationToken
		);
	}

	public static async Task TruncateStream(this IPublisher publisher, string stream, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		var last = await publisher.ReadStreamLastEvent(stream, cancellationToken);

		await publisher.TruncateStream(
			stream,
			last!.Value.OriginalEventNumber + 1,
			cancellationToken
		);
	}

	public static async Task<(string Stream, StreamRevision Revision)?> GetStreamInfoByPosition(
		this IPublisher publisher, Position position, CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();

		var result = await publisher.ReadEvent(position, cancellationToken);

		return !result.Equals(ResolvedEvent.EmptyEvent)
			? (result.OriginalStreamId, StreamRevision.FromInt64(result.OriginalEventNumber))
			: null;
	}

    /// <summary>
    /// Returns the revision of the stream at the given position.
    /// If the stream does not exist at the given position, it returns StreamRevision.Start.
    /// </summary>
    public static async Task<StreamRevision> GetStreamRevision(this IPublisher publisher, Position? position, CancellationToken cancellationToken = default) {
	    if (position is null || position == Position.Start)
            return StreamRevision.Start;

        if (position == Position.End)
            return StreamRevision.End;

        var info     = await publisher.GetStreamInfoByPosition(position.GetValueOrDefault(), cancellationToken);
        var revision = info?.Revision ?? StreamRevision.Start;

        return revision;
    }
}