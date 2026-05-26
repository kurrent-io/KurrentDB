// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.TransactionLog.LogRecords;
using Polly;

namespace KurrentDB.Kontext.Tests.Fakes;

/// <summary>
/// Minimal in-memory fake of <see cref="ISystemClient"/> used by Kontext unit tests.
/// Supports the small surface that Kontext's extension methods exercise:
/// reading a stream forwards, the last event in the log, and writing events.
/// All other operations throw <see cref="NotImplementedException"/>.
/// </summary>
public sealed class FakeSystemClient : ISystemClient {
	readonly Dictionary<string, List<ResolvedEvent>> _streamEvents = [];
	ulong? _headPosition;

	public List<PendingEvent> WrittenEvents { get; } = [];

	public IManagementOperations Management { get; }
	public IWriteOperations Writing { get; }
	public ISubscriptionsOperations Subscriptions { get; }
	public IReadOperations Reading { get; }

	public FakeSystemClient() {
		Management = new NotImplementedManagement();
		Writing = new FakeWriting(this);
		Subscriptions = new NotImplementedSubscriptions();
		Reading = new FakeReading(this);
	}

	public FakeSystemClient SetHeadPosition(ulong? commitPosition) {
		_headPosition = commitPosition;
		return this;
	}

	public FakeSystemClient AddStreamEvent(string stream, ResolvedEvent resolved) {
		if (!_streamEvents.TryGetValue(stream, out var list))
			_streamEvents[stream] = list = [];
		list.Add(resolved);
		return this;
	}

	public static ResolvedEvent MakeEvent(
		string stream, long eventNumber, string eventType,
		long commitPosition = 0, object? data = null) {
		var dataBytes = data == null
			? Array.Empty<byte>()
			: JsonSerializer.SerializeToUtf8Bytes(data);

		var record = new EventRecord(
			eventNumber: eventNumber,
			logPosition: commitPosition,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: commitPosition,
			transactionOffset: 0,
			eventStreamId: stream,
			expectedVersion: -1,
			timeStamp: new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
			flags: PrepareFlags.IsJson,
			eventType: eventType,
			data: dataBytes,
			metadata: []);

		return ResolvedEvent.ForUnresolvedEvent(record, commitPosition);
	}

	static async IAsyncEnumerable<T> AsAsync<T>(IEnumerable<T> source) {
		foreach (var item in source)
			yield return item;
		await Task.CompletedTask;
	}

	sealed class FakeReading(FakeSystemClient parent) : IReadOperations {
		public ValueTask<ResolvedEvent?> ReadLastEvent(CancellationToken cancellationToken = default) {
			if (parent._headPosition is not { } pos)
				return new ValueTask<ResolvedEvent?>((ResolvedEvent?)null);

			var record = new EventRecord(
				eventNumber: 0,
				logPosition: (long)pos,
				correlationId: Guid.NewGuid(),
				eventId: Guid.NewGuid(),
				transactionPosition: (long)pos,
				transactionOffset: 0,
				eventStreamId: "$head",
				expectedVersion: 0,
				timeStamp: DateTime.UtcNow,
				flags: PrepareFlags.None,
				eventType: "head",
				data: [],
				metadata: []);
			return new ValueTask<ResolvedEvent?>(ResolvedEvent.ForUnresolvedEvent(record, (long)pos));
		}

		public async IAsyncEnumerable<ResolvedEvent> ReadStreamForwards(
			string stream, StreamRevision startRevision, long maxCount,
			[EnumeratorCancellation] CancellationToken cancellationToken = default) {
			// Match production semantics: the throw is deferred to MoveNextAsync, not the call site.
			if (!parent._streamEvents.TryGetValue(stream, out var events))
				throw new ReadResponseException.StreamNotFound(stream);

			var startNum = startRevision.ToInt64();
			foreach (var e in events
				.Where(e => e.OriginalEvent.EventNumber >= startNum)
				.OrderBy(e => e.OriginalEvent.EventNumber)
				.Take((int)maxCount))
				yield return e;

			await Task.CompletedTask;
		}

		public IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, IEventFilter filter, long maxCount, bool forwards = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> Read(Position startPosition, long maxCount, bool forwards = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, IEventFilter filter, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public ValueTask<ResolvedEvent?> ReadFirstEvent(CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadStream(string stream, StreamRevision startRevision, long maxCount, bool forwards, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadFullStream(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadStreamBackwards(string stream, StreamRevision startRevision, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadStreamByPosition(Position startPosition, long maxCount, bool forwards, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionForwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadStreamByPositionBackwards(Position startPosition, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<ResolvedEvent?> ReadStreamLastEvent(string stream, CancellationToken cancellationToken = default) {
			if (!parent._streamEvents.TryGetValue(stream, out var events) || events.Count == 0)
				return Task.FromResult<ResolvedEvent?>(null);
			var latest = events.OrderByDescending(e => e.OriginalEvent.EventNumber).First();
			return Task.FromResult<ResolvedEvent?>(latest);
		}
		public Task<ResolvedEvent?> ReadStreamFirstEvent(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<ResolvedEvent> ReadEvent(Position position, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadIndexForwards(string indexName, Position startPosition, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public IAsyncEnumerable<ResolvedEvent> ReadIndexBackwards(string indexName, Position startPosition, long maxCount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	}

	sealed class FakeWriting(FakeSystemClient parent) : IWriteOperations {
		public Task<(Position Position, StreamRevision StreamRevision)> WriteEvents(
			string stream, Event[] events, bool requireLeader, ClaimsPrincipal principal,
			long expectedRevision = -2, CancellationToken cancellationToken = default) {
			foreach (var e in events)
				parent.WrittenEvents.Add(new PendingEvent(stream, e.EventType, e.Data));
			return Task.FromResult((new Position(0, 0), StreamRevision.Start));
		}

		public Task<(Position Position, LowAllocReadOnlyMemory<StreamRevision> StreamRevisions)> WriteEvents(
			LowAllocReadOnlyMemory<string> streams, LowAllocReadOnlyMemory<long> expectedRevisions,
			LowAllocReadOnlyMemory<Event> events, LowAllocReadOnlyMemory<int> eventStreamIndexes,
			bool requireLeader, ClaimsPrincipal principal, CancellationToken cancellationToken = default) {
			var streamArr = streams.ToArray();
			var eventArr = events.ToArray();
			var idxArr = eventStreamIndexes.ToArray();
			for (var i = 0; i < eventArr.Length; i++)
				parent.WrittenEvents.Add(new PendingEvent(streamArr[idxArr[i]], eventArr[i].EventType, eventArr[i].Data));

			var revisions = LowAllocReadOnlyMemory<StreamRevision>.Builder.Empty;
			foreach (var _ in streamArr)
				revisions = revisions.Add(StreamRevision.Start);
			return Task.FromResult((new Position(0, 0), revisions.Build()));
		}

		public Task<(Position Position, LowAllocReadOnlyMemory<StreamRevision> StreamRevisions)> WriteEvents(
			LowAllocReadOnlyMemory<StreamWrite> writes, bool requireLeader, ClaimsPrincipal principal,
			CancellationToken cancellationToken = default) {
			var revisions = LowAllocReadOnlyMemory<StreamRevision>.Builder.Empty;
			foreach (var write in writes) {
				foreach (var e in write.Events)
					parent.WrittenEvents.Add(new PendingEvent(write.Stream, e.EventType, e.Data));
				revisions = revisions.Add(StreamRevision.Start);
			}
			return Task.FromResult((new Position(0, 0), revisions.Build()));
		}
	}

	sealed class NotImplementedManagement : IManagementOperations {
		public Task<(Position Position, StreamRevision Revision)> DeleteStream(string stream, long expectedRevision = -2, bool hardDelete = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<(Position Position, StreamRevision Revision)> SoftDeleteStream(string stream, long expectedRevision = -2, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<(Position Position, StreamRevision Revision)> HardDeleteStream(string stream, long expectedRevision = -2, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<(StreamMetadata Metadata, long Revision)> SetStreamMetadata(string stream, StreamMetadata metadata, long expectedRevision = -2, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<(StreamMetadata Metadata, long Revision)> GetStreamMetadata(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<bool> StreamExists(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task TruncateStream(string stream, long beforeRevision, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task TruncateStream(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<StreamRevision> GetStreamRevision(Position position, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	}

	sealed class NotImplementedSubscriptions : ISubscriptionsOperations {
		public Task SubscribeToAll(Position? position, IEventFilter filter, uint maxSearchWindow, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task SubscribeToStream(StreamRevision? revision, string stream, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task SubscribeToIndex(Position? position, string indexName, Channel<ReadResponse> channel, ResiliencePipeline resiliencePipeline, CancellationToken cancellationToken) => throw new NotImplementedException();
	}
}