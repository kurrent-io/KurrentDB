// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
#nullable enable

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace KurrentDB.Core;

using DeleteStreamResult = (Position Position, StreamRevision Revision);
using StreamInfo = (string Stream, StreamRevision Revision);
using StreamMetadataResult = (StreamMetadata Metadata, long Revision);
using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

public interface ISystemClient {
	IManagementOperations Management { get; }
	IWriteOperations Writing { get; }
	ISubscriptionsOperations Subscriptions { get; }
	IReadOperations Reading { get; }
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
	Task<StreamRevision> GetStreamRevision(Position position, CancellationToken cancellationToken = default);
}

public interface IReadOperations {
	IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, IEventFilter filter, long maxCount, bool forwards = true, CancellationToken cancellationToken = default);
	IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, long maxCount, bool forwards = true, CancellationToken cancellationToken = default);
	IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default);
	IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default);
	IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default);
	IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default);
	ValueTask<ResolvedEvent?> ReadFirstEvent(CancellationToken cancellationToken = default);
	ValueTask<ResolvedEvent?> ReadLastEvent(CancellationToken cancellationToken = default);
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
	Task SubscribeToAll(Position? position, IEventFilter filter, uint maxSearchWindow, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken);
	Task SubscribeToStream(StreamRevision? revision, string stream, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken);
	Task SubscribeToIndex(Position? position, string indexName, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken);
}

[PublicAPI]
public class SystemClient : ISystemClient {
	public SystemClient(IPublisher publisher, ILoggerFactory? loggerFactory = null) {
		Publisher = publisher;
		Logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemClient>();

		Management = new ManagementOperations(Publisher, Logger);
		Writing = new WriteOperations(Publisher, Logger);
		Subscriptions = new SubscriptionsOperations(Publisher, Logger);
		Reading = new ReadOperations(Publisher, Logger);
	}

	IPublisher Publisher { get; }
	ILogger<SystemClient> Logger { get; }

	public IManagementOperations Management { get; private set; }
	public IWriteOperations Writing { get; private set; }
	public ISubscriptionsOperations Subscriptions { get; private set; }
	public IReadOperations Reading { get; private set; }

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
		public Task SubscribeToAll(Position? position, IEventFilter filter, uint maxSearchWindow, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) =>
			Publisher.SubscribeToAll(position, filter, maxSearchWindow, channel, resiliencePipeline, cancellationToken);
		public Task SubscribeToStream(StreamRevision? revision, string stream, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) =>
			Publisher.SubscribeToStream(revision, stream, channel, resiliencePipeline, cancellationToken);
		public Task SubscribeToIndex(Position? position, string indexName, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) =>
			Publisher.SubscribeToIndex(position, indexName, channel, resiliencePipeline, cancellationToken);
	}

	#endregion . Subscriptions .

	#region . Read .

	public record ReadOperations(IPublisher Publisher, ILogger Logger) : IReadOperations {
		public IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, IEventFilter filter, long maxCount, bool forwards = true, CancellationToken cancellationToken = default) =>
			Publisher.Read(startPosition, filter, maxCount, forwards, cancellationToken);

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

		public ValueTask<ResolvedEvent?> ReadFirstEvent(CancellationToken cancellationToken = default) =>
			Publisher.ReadFirstEvent(cancellationToken);

		public ValueTask<ResolvedEvent?> ReadLastEvent(CancellationToken cancellationToken = default) =>
			Publisher.ReadLastEvent(cancellationToken);

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

public static class SystemClientExtensions {
	public static async ValueTask<long> GetLastPosition(this ISystemClient client, CancellationToken cancellationToken = default) {
		var lastEvent = await client.Reading.ReadLastEvent(cancellationToken);
		return lastEvent is { } resolvedEvent ? resolvedEvent.OriginalPosition.GetValueOrDefault().CommitPosition : -1;
	}

	public static async ValueTask<long> GetStreamLastPosition(this ISystemClient client, string stream, CancellationToken cancellationToken = default) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "Stream cannot be null or empty");
		var lastEvent = await client.Reading.ReadStreamLastEvent(stream, cancellationToken);
		return lastEvent is { } resolvedEvent ? resolvedEvent.OriginalPosition.GetValueOrDefault().CommitPosition : -1;
	}
}
