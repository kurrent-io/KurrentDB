// ReSharper disable CheckNamespace

using System.Threading.Channels;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace EventStore.Core;

using WriteEventsResult    = (Position Position, StreamRevision StreamRevision);
using DeleteStreamResult   = (Position Position, StreamRevision Revision);
using StreamMetadataResult = (StreamMetadata Metadata, long Revision);
using StreamInfo           = (string Stream, StreamRevision Revision);

// Snippet from EventStore/src/EventStore.Core.Services.Transport.Enumerators/ReadResponseException.cs to make the code compile:
// public class StreamNotFound(string streamName) : ReadResponseException {
//     public string StreamName { get; } = streamName;
// }
//
// public class WrongExpectedRevision(string stream, long expectedRevision, long actualRevision) : ReadResponseException {
//     public string Stream { get; } = stream;
//
//     public StreamRevision ExpectedStreamRevision { get; } = StreamRevision.FromInt64(expectedRevision);
//
//     public StreamRevision ActualStreamRevision { get; } = StreamRevision.FromInt64(actualRevision);
// }

public interface ISystemClient {
    IManagementOperations    Management    { get; }
    IWriteOperations         Writing       { get; }
    ISubscriptionsOperations Subscriptions { get; }
    IReadOperations          Reading       { get; }
}

public interface IManagementOperations {
    Task<DeleteStreamResult> DeleteStream(string stream, long expectedRevision = -2, bool hardDelete = false, CancellationToken cancellationToken = default);
    Task<DeleteStreamResult> SoftDeleteStream(string stream, long expectedRevision = -2, CancellationToken cancellationToken = default);
    Task<DeleteStreamResult> HardDeleteStream(string stream, long expectedRevision = -2, CancellationToken cancellationToken = default);
    Task<StreamMetadataResult> SetStreamMetadata(string stream, StreamMetadata metadata, long expectedRevision = -2, CancellationToken cancellationToken = default);
    Task<StreamMetadataResult> GetStreamMetadata(string stream, CancellationToken cancellationToken = default);
    Task<bool> StreamExists(string stream, CancellationToken cancellationToken = default);
    Task TruncateStream(string stream, long beforeRevision, CancellationToken cancellationToken = default);
    Task TruncateStream(string stream, CancellationToken cancellationToken = default);
}

public interface IReadOperations {
    IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, IEventFilter filter, long maxCount, bool forwards = true, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, long maxCount, bool forwards = true, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadStream(string stream, StreamRevision startRevision, long maxCount, bool forwards, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadStreamForwards(string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadFullStream(string stream, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadStreamBackwards(string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadStreamByPosition(Position startPosition, long maxCount, bool forwards, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default);
    Task<ResolvedEvent?> ReadStreamLastEvent(string stream, CancellationToken cancellationToken = default);
    Task<ResolvedEvent?> ReadStreamFirstEvent(string stream, CancellationToken cancellationToken = default);
    Task<ResolvedEvent> ReadEvent(Position position, CancellationToken cancellationToken = default);
}

public interface IWriteOperations {
    Task<WriteEventsResult> WriteEvents(string stream, Event[] events, long expectedRevision = -2, CancellationToken cancellationToken = default);
}

public interface ISubscriptionsOperations {
    Task SubscribeToAll(Position? position, IEventFilter filter, Channel<ResolvedEvent> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken);
}

[PublicAPI]
public class SystemClient : ISystemClient {
	public SystemClient(IPublisher publisher, ILoggerFactory? loggerFactory = null) {
		Publisher = publisher;
		Logger    = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemClient>();

		Management    = new ManagementOperations(Publisher, Logger);
		Writing       = new WriteOperations(Publisher, Logger);
		Subscriptions = new SubscriptionsOperations(Publisher, Logger);
		Reading       = new ReadOperations(Publisher, Logger);
	}

	IPublisher            Publisher { get; }
	ILogger<SystemClient> Logger    { get; }

	public IManagementOperations    Management    { get; private set; }
	public IWriteOperations         Writing       { get; private set; }
	public ISubscriptionsOperations Subscriptions { get; private set; }
	public IReadOperations          Reading       { get; private set; }

	#region . Management .

    public record ManagementOperations(IPublisher Publisher, ILogger Logger) : IManagementOperations {
		public Task<DeleteStreamResult> DeleteStream(
			string stream, long expectedRevision = -2, bool hardDelete = false, CancellationToken cancellationToken = default
		) => Publisher.DeleteStream(stream, expectedRevision, hardDelete, cancellationToken);

		public Task<DeleteStreamResult> SoftDeleteStream(
			string stream, long expectedRevision = -2, CancellationToken cancellationToken = default
		) => Publisher.DeleteStream(stream, expectedRevision, false, cancellationToken);

		public Task<DeleteStreamResult> HardDeleteStream(
			string stream, long expectedRevision = -2, CancellationToken cancellationToken = default
		) => Publisher.DeleteStream(stream, expectedRevision, true, cancellationToken);

		public Task<StreamMetadataResult> SetStreamMetadata(
			string stream, StreamMetadata metadata, long expectedRevision = -2, CancellationToken cancellationToken = default
		) => Publisher.SetStreamMetadata(stream, metadata, expectedRevision, cancellationToken);

		public Task<StreamMetadataResult> GetStreamMetadata(string stream, CancellationToken cancellationToken = default) =>
			Publisher.GetStreamMetadata(stream, cancellationToken);

		public Task<bool> StreamExists(string stream, CancellationToken cancellationToken = default) =>
			Publisher.StreamExists(stream, cancellationToken);

		public Task TruncateStream(string stream, long beforeRevision, CancellationToken cancellationToken = default) =>
			Publisher.TruncateStream(stream, beforeRevision, cancellationToken);

		public Task TruncateStream(string stream, CancellationToken cancellationToken = default) =>
			Publisher.TruncateStream(stream, cancellationToken);

		public Task<StreamInfo?> GetStreamInfoByPosition(Position position, CancellationToken cancellationToken = default) =>
			Publisher.GetStreamInfoByPosition(position, cancellationToken);

        public Task<StreamRevision> GetStreamRevision(Position position, CancellationToken cancellationToken = default) =>
            Publisher.GetStreamRevision(position, cancellationToken);
	}

	#endregion . Management .

	#region . Write .

    public record WriteOperations(IPublisher Publisher, ILogger Logger) : IWriteOperations {
		public Task<WriteEventsResult> WriteEvents(string stream, Event[] events, long expectedRevision = -2, CancellationToken cancellationToken = default) =>
			Publisher.WriteEvents(stream, events, expectedRevision, cancellationToken);
	}

	#endregion . Write .

	#region . Subscriptions .

    public record SubscriptionsOperations(IPublisher Publisher, ILogger Logger) : ISubscriptionsOperations {
		public Task SubscribeToAll(Position? position, IEventFilter filter, Channel<ResolvedEvent> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) =>
			Publisher.SubscribeToAll(position, filter, channel, resiliencePipeline, cancellationToken);
	}

	#endregion . Subscriptions .

	#region . Read .

    public record ReadOperations(IPublisher Publisher, ILogger Logger) : IReadOperations {
        public IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, IEventFilter filter, long maxCount, bool forwards = true, CancellationToken cancellationToken = default) =>
            Publisher.Read(startPosition, maxCount, forwards, cancellationToken);

        public IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default) =>
            Publisher.Read(startPosition, filter, maxCount, true, cancellationToken);

        public IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default) =>
            Publisher.Read(startPosition, filter, maxCount, false, cancellationToken);

        public IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, long maxCount, bool forwards = true, CancellationToken cancellationToken = default) =>
			Publisher.Read(startPosition, maxCount, forwards, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
			Publisher.Read(startPosition, maxCount, true, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
			Publisher.Read(startPosition, maxCount, false, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadStream(string stream, StreamRevision startRevision, long maxCount, bool forwards, CancellationToken cancellationToken = default) =>
			Publisher.ReadStream(stream, startRevision, maxCount, forwards, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadStreamForwards(string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default) =>
			Publisher.ReadStream(stream, startRevision, maxCount, true, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadFullStream(string stream, CancellationToken cancellationToken = default) =>
			Publisher.ReadStream(stream, StreamRevision.Start, long.MaxValue, true, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadStreamBackwards(string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default) =>
			Publisher.ReadStream(stream, startRevision, maxCount, false, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadStreamByPosition(Position startPosition, long maxCount, bool forwards, CancellationToken cancellationToken = default) =>
			Publisher.ReadStreamByPosition(startPosition, maxCount, forwards, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
			Publisher.ReadStreamByPosition(startPosition, maxCount, true, cancellationToken);

		public IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) =>
			Publisher.ReadStreamByPosition(startPosition, maxCount, false, cancellationToken);

		public Task<ResolvedEvent?> ReadStreamLastEvent(string stream, CancellationToken cancellationToken = default) =>
			Publisher.ReadStreamLastEvent(stream, cancellationToken);

		public Task<ResolvedEvent?> ReadStreamFirstEvent(string stream, CancellationToken cancellationToken = default) =>
			Publisher.ReadStreamFirstEvent(stream, cancellationToken);

		public Task<ResolvedEvent> ReadEvent(Position position, CancellationToken cancellationToken = default) =>
			Publisher.ReadEvent(position, cancellationToken);
	}

	#endregion . Read .
}