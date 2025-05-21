// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using ILogger = Serilog.ILogger;

// ReSharper disable InvertIf

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public interface IIndexReader<TStreamId> {
	long CachedStreamInfo { get; }
	long NotCachedStreamInfo { get; }
	long HashCollisions { get; }

	// streamId drives the read, streamName is only for populating on the result.
	// this was less messy than safely adding the streamName to the EventRecord at some point after construction
	ValueTask<IndexReadEventResult> ReadEvent(string streamName, TStreamId streamId, long eventNumber, CancellationToken token);
	ValueTask<IndexReadStreamResult> ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token);
	ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token);
	ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(TStreamId streamId, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);

	/// <summary>
	/// Doesn't filter $maxAge, $maxCount, $tb(truncate before), doesn't check stream deletion, etc.
	/// </summary>
	ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepare(TStreamId streamId, long eventNumber, CancellationToken token);

	ValueTask<TStreamId> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token);

	ValueTask<StreamMetadata> GetStreamMetadata(TStreamId streamId, CancellationToken token);
	ValueTask<long> GetStreamLastEventNumber(TStreamId streamId, CancellationToken token);
	ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, CancellationToken token);
	ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition, CancellationToken token);
	TFReaderLease BorrowReader();
}

public abstract class IndexReader {
	public static string UnspecifiedStreamName => "Unspecified stream name";
	protected static readonly ILogger Log = Serilog.Log.ForContext<IndexReader>();
}

public class IndexReader<TStreamId> : IndexReader, IIndexReader<TStreamId> {
	private static EqualityComparer<TStreamId> StreamIdComparer { get; } = EqualityComparer<TStreamId>.Default;

	public long CachedStreamInfo {
		get { return Interlocked.Read(ref _cachedStreamInfo); }
	}

	public long NotCachedStreamInfo {
		get { return Interlocked.Read(ref _notCachedStreamInfo); }
	}

	public long HashCollisions {
		get { return Interlocked.Read(ref _hashCollisions); }
	}

	private readonly IIndexBackend<TStreamId> _backend;
	private readonly ITableIndex<TStreamId> _tableIndex;
	private readonly INameLookup<TStreamId> _eventTypes;
	private readonly ISystemStreamLookup<TStreamId> _systemStreams;
	private readonly IValidator<TStreamId> _validator;
	private readonly IExistenceFilterReader<TStreamId> _streamExistenceFilter;
	private readonly bool _skipIndexScanOnRead;
	private readonly StreamMetadata _metastreamMetadata;

	private long _hashCollisions;
	private long _cachedStreamInfo;
	private long _notCachedStreamInfo;
	private readonly int _hashCollisionReadLimit;

	public IndexReader(
		IIndexBackend<TStreamId> backend,
		ITableIndex<TStreamId> tableIndex,
		IStreamNamesProvider<TStreamId> streamNamesProvider,
		IValidator<TStreamId> validator,
		IExistenceFilterReader<TStreamId> streamExistenceFilter,
		StreamMetadata metastreamMetadata,
		int hashCollisionReadLimit, bool skipIndexScanOnRead) {
		Ensure.NotNull(streamNamesProvider);
		streamNamesProvider.SetReader(this);
		_backend = Ensure.NotNull(backend);
		_tableIndex = Ensure.NotNull(tableIndex);
		_systemStreams = streamNamesProvider.SystemStreams;
		_eventTypes = streamNamesProvider.EventTypes;
		_validator = Ensure.NotNull(validator);
		_streamExistenceFilter = streamExistenceFilter;
		_metastreamMetadata = Ensure.NotNull(metastreamMetadata);
		_hashCollisionReadLimit = hashCollisionReadLimit;
		_skipIndexScanOnRead = skipIndexScanOnRead;
	}

	async ValueTask<IndexReadEventResult> IIndexReader<TStreamId>.ReadEvent(string streamName, TStreamId streamId, long eventNumber, CancellationToken token) {
		Ensure.Valid(streamId, _validator);
		ArgumentOutOfRangeException.ThrowIfLessThan(eventNumber, -1);

		using var reader = _backend.BorrowReader();
		return await ReadEventInternal(reader, streamName, streamId, eventNumber, token);
	}

	private async ValueTask<IndexReadEventResult> ReadEventInternal(TFReaderLease reader, string streamName, TStreamId streamId, long eventNumber, CancellationToken token) {
		var lastEventNumber = await GetStreamLastEventNumberCached(reader, streamId, token);
		var metadata = await GetStreamMetadataCached(reader, streamId, token);
		var originalStreamExists = await OriginalStreamExists(reader, streamId, token);
		if (lastEventNumber == EventNumber.DeletedStream)
			return new(ReadEventResult.StreamDeleted, metadata, lastEventNumber, originalStreamExists);
		if (lastEventNumber == ExpectedVersion.NoStream || metadata.TruncateBefore == EventNumber.DeletedStream)
			return new(ReadEventResult.NoStream, metadata, lastEventNumber, originalStreamExists);
		if (lastEventNumber == EventNumber.Invalid)
			return new(ReadEventResult.NoStream, metadata, lastEventNumber, originalStreamExists);

		if (eventNumber == -1)
			eventNumber = lastEventNumber;

		long minEventNumber = 0;
		if (metadata.MaxCount.HasValue)
			minEventNumber = Math.Max(minEventNumber, lastEventNumber - metadata.MaxCount.Value + 1);
		if (metadata.TruncateBefore.HasValue)
			minEventNumber = Math.Max(minEventNumber, metadata.TruncateBefore.Value);
		//TODO(clc): confirm this logic, it seems that reads less than min should be invaild rather than found
		if (eventNumber < minEventNumber || eventNumber > lastEventNumber)
			return new(ReadEventResult.NotFound, metadata, lastEventNumber, originalStreamExists);

		if (await ReadPrepareInternal(reader, streamId, eventNumber, token) is { } prepare) {
			if (metadata.MaxAge.HasValue && prepare.TimeStamp < DateTime.UtcNow - metadata.MaxAge.Value)
				return new(ReadEventResult.NotFound, metadata, lastEventNumber, originalStreamExists);
			return new(ReadEventResult.Success, await CreateEventRecord(eventNumber, prepare, streamName, token), metadata, lastEventNumber, originalStreamExists);
		}

		return new(ReadEventResult.NotFound, metadata, lastEventNumber, originalStreamExists: originalStreamExists);
	}

	async ValueTask<IPrepareLogRecord<TStreamId>> IIndexReader<TStreamId>.ReadPrepare(TStreamId streamId, long eventNumber, CancellationToken token) {
		using var reader = _backend.BorrowReader();
		return await ReadPrepareInternal(reader, streamId, eventNumber, token);
	}

	private ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepareInternal(TFReaderLease reader, TStreamId streamId, long eventNumber, CancellationToken token) {
		// we assume that you already did check for stream deletion
		Ensure.Valid(streamId, _validator);
		Ensure.Nonnegative(eventNumber);

		return _skipIndexScanOnRead
			? ReadPrepareSkipScan(reader, streamId, eventNumber, token)
			: ReadPrepare(reader, streamId, eventNumber, token);
	}

	private ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepare(TFReaderLease reader, TStreamId streamId,
		long eventNumber, CancellationToken token) {
		var recordsQuery = _tableIndex.GetRange(streamId, eventNumber, eventNumber)
			.ToAsyncEnumerable()
			.SelectAwaitWithCancellation(async (x, token) =>
				new { x.Version, Prepare = await ReadPrepareInternal(reader, x.Position, token) })
			.Where(x => x.Prepare is not null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamId))
			.GroupBy(static x => x.Version)
			.SelectAwaitWithCancellation(AsyncEnumerable.LastAsync)
			.Select(static x => x.Prepare);

		return recordsQuery.FirstOrDefaultAsync(token);
	}

	private async ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepareSkipScan(TFReaderLease reader, TStreamId streamId, long eventNumber, CancellationToken token) {
		if (!_tableIndex.TryGetOneValue(streamId, eventNumber, out var position)) {
			return null;
		}

		if (await ReadPrepareInternal(reader, position, token) is { } rec && StreamIdComparer.Equals(rec.EventStreamId, streamId))
			return rec;

		foreach (var indexEntry in _tableIndex.GetRange(streamId, eventNumber, eventNumber)) {
			Interlocked.Increment(ref _hashCollisions);
			if (indexEntry.Position == position)
				continue;
			rec = await ReadPrepareInternal(reader, indexEntry.Position, token);
			if (rec != null && StreamIdComparer.Equals(rec.EventStreamId, streamId))
				return rec;
		}

		return null;
	}

	private static async ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepareInternal(TFReaderLease reader, long logPosition, CancellationToken token) {
		var result = await reader.TryReadAt(logPosition, couldBeScavenged: true, token);
		if (!result.Success)
			return null;

		if (result.LogRecord.RecordType is not LogRecordType.Prepare
			and not LogRecordType.Stream
			and not LogRecordType.EventType)
			throw new Exception($"Incorrect type of log record {result.LogRecord.RecordType}, expected Prepare record.");
		return (IPrepareLogRecord<TStreamId>)result.LogRecord;
	}

	ValueTask<IndexReadStreamResult> IIndexReader<TStreamId>.ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token) {
		return ReadStreamEventsForwardInternal(streamName, streamId, fromEventNumber, maxCount, _skipIndexScanOnRead, token);
	}

	private async ValueTask<IndexReadStreamResult> ReadStreamEventsForwardInternal(string streamName, TStreamId streamId, long fromEventNumber,
		int maxCount, bool skipIndexScanOnRead, CancellationToken token) {
		Ensure.Valid(streamId, _validator);
		Ensure.Nonnegative(fromEventNumber, "fromEventNumber");
		Ensure.Positive(maxCount, "maxCount");

		using (var reader = _backend.BorrowReader()) {
			var lastEventNumber = await GetStreamLastEventNumberCached(reader, streamId, token);
			var metadata = await GetStreamMetadataCached(reader, streamId, token);
			if (lastEventNumber is EventNumber.DeletedStream)
				return new(fromEventNumber, maxCount, ReadStreamResult.StreamDeleted, StreamMetadata.Empty, lastEventNumber);
			if (lastEventNumber == ExpectedVersion.NoStream || metadata.TruncateBefore == EventNumber.DeletedStream)
				return new(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata, lastEventNumber);
			if (lastEventNumber == EventNumber.Invalid)
				return new(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata, lastEventNumber);

			long startEventNumber = fromEventNumber;
			long endEventNumber = fromEventNumber < long.MaxValue - maxCount + 1 ? fromEventNumber + maxCount - 1 : long.MaxValue;

			// calculate minEventNumber, which is the lower bound that we are allowed to read
			// according to the MaxCount and TruncateBefore
			long minEventNumber = 0;
			if (metadata.MaxCount.HasValue)
				minEventNumber = Math.Max(minEventNumber, lastEventNumber - metadata.MaxCount.Value + 1);
			if (metadata.TruncateBefore.HasValue)
				minEventNumber = Math.Max(minEventNumber, metadata.TruncateBefore.Value);

			// early return if we are trying to read events that are all below the lower bound
			if (endEventNumber < minEventNumber)
				return new(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords, metadata, minEventNumber, lastEventNumber, isEndOfStream: false);

			// start our read at the lower bound if we were going to start it before hand.
			startEventNumber = Math.Max(startEventNumber, minEventNumber);

			// early return if we are trying to read events that are all above the upper bound
			if (startEventNumber > lastEventNumber)
				return new(
					fromEventNumber: fromEventNumber,
					maxCount: maxCount,
					records: IndexReadStreamResult.EmptyRecords,
					metadata: metadata,
					nextEventNumber: lastEventNumber + 1,
					lastEventNumber: lastEventNumber,
					isEndOfStream: true);

			if (metadata.MaxAge.HasValue) {
				return await ForStreamWithMaxAge(streamId, streamName,
					fromEventNumber, maxCount,
					startEventNumber, endEventNumber, lastEventNumber,
					metadata.MaxAge.Value, metadata, _tableIndex, reader, _eventTypes,
					skipIndexScanOnRead, token);
			}

			var recordsQuery = _tableIndex.GetRange(streamId, startEventNumber, endEventNumber)
				.ToAsyncEnumerable()
				.SelectAwaitWithCancellation(async (x, ct) => new { x.Version, Prepare = await ReadPrepareInternal(reader, x.Position, ct) })
				.Where(x => x.Prepare != null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamId));
			if (!skipIndexScanOnRead) {
				recordsQuery = recordsQuery.OrderByDescending(x => x.Version)
					.GroupBy(static x => x.Version).SelectAwaitWithCancellation(AsyncEnumerable.LastAsync);
			}

			var records = await recordsQuery
				.Reverse()
				.SelectAwaitWithCancellation((x, ct) => CreateEventRecord(x.Version, x.Prepare, streamName, ct))
				.ToArrayAsync(token);

			long nextEventNumber = Math.Min(endEventNumber + 1, lastEventNumber + 1);
			if (records.Length > 0)
				nextEventNumber = records[^1].EventNumber + 1;
			var isEndOfStream = endEventNumber >= lastEventNumber;
			return new(fromEventNumber, maxCount, records, metadata, nextEventNumber, lastEventNumber, isEndOfStream);
		}

		async ValueTask<IndexReadStreamResult> ForStreamWithMaxAge(TStreamId streamId,
			string streamName,
			long fromEventNumber, int maxCount, long startEventNumber,
			long endEventNumber, long lastEventNumber, TimeSpan maxAge, StreamMetadata metadata,
			ITableIndex<TStreamId> tableIndex, TFReaderLease reader, INameLookup<TStreamId> eventTypes,
			bool skipIndexScanOnRead,
			CancellationToken token) {
			if (startEventNumber > lastEventNumber) {
				return new(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
					metadata, lastEventNumber + 1, lastEventNumber, isEndOfStream: true);
			}

			var ageThreshold = DateTime.UtcNow - maxAge;
			var nextEventNumber = lastEventNumber;
			var indexEntries = tableIndex.GetRange(streamId, startEventNumber, endEventNumber);

			//Move to the first valid entries. At this point we could instead return an empty result set with the minimum set, but that would
			//involve an additional set of reads for no good reason
			while (indexEntries is []) {
				// we didn't find any indexEntries, and we already early returned in the case that startEventNumber > lastEventNumber
				// so we want to find the oldest entry that is after startEventNumber.
				// startEventNumber may be before the start of the stream, or it may be in a gap caused by scavenging.
				// this will generally only iterate once, unless a scavenge completes exactly now, in which case it might iterate twice
				if (tableIndex.TryGetNextEntry(streamId, startEventNumber, out var next)) {
					startEventNumber = next.Version;
					endEventNumber = startEventNumber + maxCount - 1;
					indexEntries = tableIndex.GetRange(streamId, startEventNumber, endEventNumber);
				} else {
					//scavenge completed and deleted our stream? return empty set and get the client to try again?
					return new(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
						metadata, lastEventNumber + 1, lastEventNumber, isEndOfStream: false);
				}
			}

			var results = new ResultsDeduplicator(maxCount, skipIndexScanOnRead);

			for (int i = 0; i < indexEntries.Count; i++) {
				if (await ReadPrepareInternal(reader, indexEntries[i].Position, token) is not { } prepare ||
					!StreamIdComparer.Equals(prepare.EventStreamId, streamId)) {
					continue;
				}

				if (prepare.TimeStamp >= ageThreshold) {
					results.Add(await CreateEventRecord(indexEntries[i].Version, prepare, streamName, eventTypes, token));
				} else {
					break;
				}
			}

			if (results.Count > 0) {
				//We got at least one event in the correct age range, so we will return whatever was valid and indicate where to read from next
				var resultsArray = results.ProduceArray();
				nextEventNumber = resultsArray[0].EventNumber + 1;
				Array.Reverse(resultsArray);

				var isEndOfStream = endEventNumber >= lastEventNumber;
				return new(fromEventNumber, maxCount, resultsArray, metadata, nextEventNumber, lastEventNumber, isEndOfStream);
			}

			//we didn't find anything valid yet, now we need to search
			//the entries we found were all either scavenged, or expired, or for another stream.

			//check high value will be valid, otherwise early return.
			// this resolves hash collisions itself
			if (await ReadPrepareInternal(reader, streamId, eventNumber: lastEventNumber, token) is not { } lastEvent ||
				lastEvent.TimeStamp < ageThreshold || lastEventNumber < fromEventNumber) {
				//No events in the stream are < max age, so return an empty set
				return new(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords, metadata, lastEventNumber + 1, lastEventNumber, isEndOfStream: true);
			}

			// intuition for loop termination here is that the explicit continue cases are
			// the only way to continue the iteration. apart from those it return;s or break;s.
			// the continue cases always narrow the gap between low and high
			//
			// low, mid, and high are event numbers (versions)
			// attempt to initialize low to the latest (highest) entry that we found but in the unlikely event that
			// they're out of version order that's still ok, low will just be lower than it could have been.
			var low = indexEntries[0].Version;
			var high = lastEventNumber;
			while (low <= high) {
				var mid = low + ((high - low) / 2);
				indexEntries = tableIndex.GetRange(streamId, mid, mid + maxCount - 1);
				if (indexEntries.Count > 0) {
					nextEventNumber = indexEntries[0].Version + 1;
				} else {
					// midpoint is in a gap. everything before any gap is no longer part of the stream
					// though the entries may not have been removed yet. skip to higher event numbers
					if (!tableIndex.TryGetNextEntry(streamId, mid, out var next)) {
						// there is nothing in this stream (maybe a scavenge just completed)
						nextEventNumber = lastEventNumber;
						break;
					}

					low = next.Version;
					continue;
				}

				// be really careful if adjusting these, to make sure that the loop still terminates
				var (lowPrepareVersion, lowPrepare) = await LowPrepare(reader, indexEntries, streamId, token);
				if (lowPrepare?.TimeStamp >= ageThreshold) {
					// found a lowPrepare for this stream. it has not expired. chop lower in case there are more
					high = mid - 1;
					nextEventNumber = lowPrepareVersion;
					continue;
				}

				var (highPrepareVersion, highPrepare) = await HighPrepare(reader, indexEntries, streamId, token);
				if (highPrepare?.TimeStamp < ageThreshold) {
					// found a highPrepare for this stream. it has expired. chop higher
					low = highPrepareVersion + 1;
					continue;
				}

				//ok, some entries must match, if not (due to time moving forwards) we can just reissue based on the current mid
				// also might have no matches because the getrange call returned addresses that
				// were all scavenged or for other streams, in which case we won't add anything to results here
				for (int i = 0; i < indexEntries.Count; i++) {
					if (await ReadPrepareInternal(reader, indexEntries[i].Position, token) is not { } prepare ||
						!StreamIdComparer.Equals(prepare.EventStreamId, streamId))
						continue;

					if (prepare?.TimeStamp >= ageThreshold) {
						results.Add(await CreateEventRecord(indexEntries[i].Version, prepare, streamName, eventTypes, token));
					} else {
						break;
					}
				}

				if (results.Count > 0) {
					//We got at least one event in the correct age range, so we will return whatever was valid and indicate where to read from next
					var resultsList = results.ProduceList();
					endEventNumber = resultsList[0].EventNumber;
					nextEventNumber = endEventNumber + 1;
					resultsList.Reverse();
					var isEndOfStream = endEventNumber >= lastEventNumber;

					var maxEventNumberToReturn = fromEventNumber + maxCount - 1;
					while (resultsList.Count > 0 && resultsList[^1].EventNumber > maxEventNumberToReturn) {
						nextEventNumber = resultsList[^1].EventNumber;
						resultsList.RemoveAt(resultsList.Count - 1);
						isEndOfStream = false;
					}

					return new(fromEventNumber, maxCount, resultsList.ToArray(), metadata, nextEventNumber, lastEventNumber, isEndOfStream);
				}

				break;
			}

			//We didn't find anything, send back to the client with the latest position to retry
			return new(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords, metadata, nextEventNumber, lastEventNumber, isEndOfStream: false);

			static async ValueTask<(long, IPrepareLogRecord<TStreamId>)> LowPrepare(
				TFReaderLease tfReaderLease,
				IReadOnlyList<IndexEntry> entries,
				TStreamId streamId,
				CancellationToken token) {
				for (int i = entries.Count - 1; i >= 0; i--) {
					if (await ReadPrepareInternal(tfReaderLease, entries[i].Position, token) is { } prepare && StreamIdComparer.Equals(prepare.EventStreamId, streamId))
						return (entries[i].Version, prepare);
				}

				return default;
			}

			static async ValueTask<(long, IPrepareLogRecord<TStreamId>)> HighPrepare(
				TFReaderLease tfReaderLease,
				IReadOnlyList<IndexEntry> entries,
				TStreamId streamId,
				CancellationToken token) {
				for (int i = 0; i < entries.Count; i++) {
					if (await ReadPrepareInternal(tfReaderLease, entries[i].Position, token) is { } prepare && StreamIdComparer.Equals(prepare.EventStreamId, streamId))
						return (entries[i].Version, prepare);
				}

				return default;
			}
		}
	}

	delegate IAsyncEnumerable<IndexEntry> ReadIndexEntries<in TStreamHandle>(
		IndexReader<TStreamId> indexReader,
		TStreamHandle streamHandle,
		TFReaderLease reader,
		long startEventNumber,
		long endEventNumber);

	private static IAsyncEnumerable<IndexEntry> ReadIndexEntries_RemoveCollisions(IndexReader<TStreamId> indexReader,
		TStreamId streamHandle,
		TFReaderLease reader,
		long startEventNumber,
		long endEventNumber)
		=> indexReader._tableIndex.GetRange(streamHandle, startEventNumber, endEventNumber)
			.ToAsyncEnumerable()
			.SelectAwaitWithCancellation(async (x, token) => new { IndexEntry = x, Prepare = await ReadPrepareInternal(reader, x.Position, token) })
			.Where(x => x.Prepare is not null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamHandle))
			.Select(static x => x.IndexEntry);

	private static IAsyncEnumerable<IndexEntry> ReadIndexEntries_NoCollisions(IndexReader<TStreamId> indexReader,
		ulong streamHandle,
		TFReaderLease reader,
		long startEventNumber,
		long endEventNumber) =>
		indexReader._tableIndex.GetRange(streamHandle, startEventNumber, endEventNumber).ToAsyncEnumerable();

	public async ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber, CancellationToken token) {
		using var reader = _backend.BorrowReader();
		var result = await ReadEventInfoForwardInternal(
			streamId,
			reader,
			ReadIndexEntries_RemoveCollisions,
			// the next event number doesn't matter in this context since we're reading a single event without a specific direction
			getNextEventNumber: static (_, _, _) => -1,
			fromEventNumber: eventNumber,
			maxCount: 1,
			beforePosition: long.MaxValue,
			deduplicate: false,
			token);

		// ensure that the next event number is set to -1
		return new IndexReadEventInfoResult(result.EventInfos, nextEventNumber: -1);
	}

	// note for simplicity skipIndexScanOnRead is always treated as false. see ReadEventInfoInternal
	public async ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token) {
		using var reader = _backend.BorrowReader();
		return await ReadEventInfoForwardInternal(
			streamId,
			reader,
			ReadIndexEntries_RemoveCollisions,
			static (self, streamHandle, afterEventNumber) => {
				if (!self._tableIndex.TryGetNextEntry(streamHandle, afterEventNumber, out var entry))
					return -1;

				// Note that this event number may be for a colliding stream. It is not a major issue since these
				// colliding events will be filtered out during the next read. However, it may cause some extra empty reads.
				return entry.Version;
			},
			fromEventNumber,
			maxCount,
			beforePosition,
			deduplicate: true,
			token);
	}

	// note for simplicity skipIndexScanOnRead is always treated as false. see ReadEventInfoInternal
	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token) {
		return ReadEventInfoForwardInternal(
			stream,
			default,
			ReadIndexEntries_NoCollisions,
			static (self, streamHandle, afterEventNumber) => {
				if (!self._tableIndex.TryGetNextEntry(streamHandle, afterEventNumber, out var entry))
					return -1;

				return entry.Version;
			},
			fromEventNumber,
			maxCount,
			beforePosition,
			deduplicate: true,
			token);
	}

	private async ValueTask<IndexReadEventInfoResult> ReadEventInfoForwardInternal<TStreamHandle>(
		TStreamHandle streamHandle,
		TFReaderLease reader,
		ReadIndexEntries<TStreamHandle> readIndexEntries,
		Func<IndexReader<TStreamId>, TStreamHandle, long, long> getNextEventNumber,
		long fromEventNumber,
		int maxCount,
		long beforePosition,
		bool deduplicate,
		CancellationToken token) {
		Ensure.Nonnegative(fromEventNumber, nameof(fromEventNumber));
		Ensure.Positive(maxCount, nameof(maxCount));

		var endEventNumber = fromEventNumber > long.MaxValue - maxCount + 1 ? long.MaxValue : fromEventNumber + maxCount - 1;

		var eventInfos = await ReadEventInfoInternal(streamHandle, reader, readIndexEntries, fromEventNumber, endEventNumber, beforePosition, deduplicate, token);

		Array.Reverse(eventInfos);

		long nextEventNumber;
		if (endEventNumber >= long.MaxValue)
			nextEventNumber = -1;
		else if (eventInfos.Length == 0)
			nextEventNumber = getNextEventNumber(this, streamHandle, endEventNumber);
		else
			nextEventNumber = endEventNumber + 1;

		return new(eventInfos, nextEventNumber);
	}

	ValueTask<IndexReadStreamResult> IIndexReader<TStreamId>.ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token) {
		return ReadStreamEventsBackwardInternal(streamName, streamId, fromEventNumber, maxCount, _skipIndexScanOnRead, token);
	}

	private async ValueTask<IndexReadStreamResult> ReadStreamEventsBackwardInternal(string streamName, TStreamId streamId, long fromEventNumber,
		int maxCount, bool skipIndexScanOnRead, CancellationToken token) {
		Ensure.Valid(streamId, _validator);
		Ensure.Positive(maxCount);

		using var reader = _backend.BorrowReader();
		var lastEventNumber = await GetStreamLastEventNumberCached(reader, streamId, token);
		var metadata = await GetStreamMetadataCached(reader, streamId, token);
		if (lastEventNumber is EventNumber.DeletedStream)
			return new(fromEventNumber, maxCount, ReadStreamResult.StreamDeleted, StreamMetadata.Empty, lastEventNumber);
		if (lastEventNumber is ExpectedVersion.NoStream || metadata.TruncateBefore is EventNumber.DeletedStream)
			return new(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata, lastEventNumber);
		if (lastEventNumber is EventNumber.Invalid)
			return new(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata, lastEventNumber);

		long endEventNumber = fromEventNumber < 0 ? lastEventNumber : fromEventNumber;
		long startEventNumber = Math.Max(0L, endEventNumber - maxCount + 1);
		bool isEndOfStream = false;

		long minEventNumber = 0;
		if (metadata.MaxCount.HasValue)
			minEventNumber = Math.Max(minEventNumber, lastEventNumber - metadata.MaxCount.Value + 1);
		if (metadata.TruncateBefore.HasValue)
			minEventNumber = Math.Max(minEventNumber, metadata.TruncateBefore.Value);
		if (endEventNumber < minEventNumber)
			return new(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords, metadata, -1, lastEventNumber, isEndOfStream: true);

		if (startEventNumber <= minEventNumber) {
			isEndOfStream = true;
			startEventNumber = minEventNumber;
		}

		var recordsQuery = _tableIndex.GetRange(streamId, startEventNumber, endEventNumber)
			.ToAsyncEnumerable()
			.SelectAwaitWithCancellation(async (x, ct) => new { x.Version, Prepare = await ReadPrepareInternal(reader, x.Position, ct) })
			.Where(x => x.Prepare is not null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamId));
		if (!skipIndexScanOnRead) {
			recordsQuery = recordsQuery
				.OrderByDescending(static x => x.Version)
				.GroupBy(x => x.Version)
				.SelectAwaitWithCancellation(AsyncEnumerable.LastAsync);
		}

		if (metadata.MaxAge.HasValue) {
			var ageThreshold = DateTime.UtcNow - metadata.MaxAge.Value;
			recordsQuery = recordsQuery.Where(x => x.Prepare.TimeStamp >= ageThreshold);
		}

		var records = await recordsQuery
			.SelectAwaitWithCancellation((x, ct) => CreateEventRecord(x.Version, x.Prepare, streamName, ct))
			.ToArrayAsync(token);

		isEndOfStream = isEndOfStream
						|| startEventNumber == 0
						|| (startEventNumber <= lastEventNumber
							&& (records.Length is 0 || records[^1].EventNumber != startEventNumber));
		long nextEventNumber = isEndOfStream ? -1 : Math.Min(startEventNumber - 1, lastEventNumber);
		return new(endEventNumber, maxCount, records, metadata, nextEventNumber, lastEventNumber, isEndOfStream);
	}

	public async ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token) {
		if (fromEventNumber < 0)
			fromEventNumber = await GetStreamLastEventNumber_KnownCollisions(streamId, beforePosition, token);

		if (fromEventNumber is ExpectedVersion.NoStream)
			return new([], -1);

		using var reader = _backend.BorrowReader();
		return await ReadEventInfoBackwardInternal(
			streamId,
			reader,
			ReadIndexEntries_RemoveCollisions,
			// Note that this event number may be for a colliding stream. It is not a major issue since these
			// colliding events will be filtered out during the next read. However, it may cause some extra empty reads.
			static (self, streamHandle, beforeEventNumber) => !self._tableIndex.TryGetPreviousEntry(streamHandle, beforeEventNumber, out var entry) ? -1 : entry.Version,
			fromEventNumber,
			maxCount,
			beforePosition,
			deduplicate: true,
			token);
	}

	public async ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(
		ulong stream,
		Func<ulong, TStreamId> getStreamId,
		long fromEventNumber,
		int maxCount,
		long beforePosition,
		CancellationToken token) {
		if (fromEventNumber < 0)
			fromEventNumber = await GetStreamLastEventNumber_NoCollisions(stream, getStreamId, beforePosition, token);

		if (fromEventNumber is ExpectedVersion.NoStream)
			return new([], -1);

		return await ReadEventInfoBackwardInternal(
			stream,
			default,
			ReadIndexEntries_NoCollisions,
			static (self, streamHandle, beforeEventNumber) => !self._tableIndex.TryGetPreviousEntry(streamHandle, beforeEventNumber, out var entry) ? -1 : entry.Version,
			fromEventNumber,
			maxCount,
			beforePosition,
			deduplicate: true,
			token);
	}

	private async ValueTask<IndexReadEventInfoResult> ReadEventInfoBackwardInternal<TStreamHandle>(
		TStreamHandle streamHandle,
		TFReaderLease reader,
		ReadIndexEntries<TStreamHandle> readIndexEntries,
		Func<IndexReader<TStreamId>, TStreamHandle, long, long> getNextEventNumber,
		long fromEventNumber,
		int maxCount,
		long beforePosition,
		bool deduplicate,
		CancellationToken token) {
		Ensure.Nonnegative(fromEventNumber, nameof(fromEventNumber));
		Ensure.Positive(maxCount, nameof(maxCount));

		var startEventNumber = Math.Max(0L, fromEventNumber - maxCount + 1);

		var eventInfos = await ReadEventInfoInternal(streamHandle, reader, readIndexEntries, startEventNumber, fromEventNumber, beforePosition, deduplicate, token);

		long nextEventNumber;
		if (startEventNumber <= 0)
			nextEventNumber = -1;
		else if (eventInfos.Length is 0)
			nextEventNumber = getNextEventNumber(this, streamHandle, startEventNumber);
		else
			nextEventNumber = startEventNumber - 1;

		return new(eventInfos, nextEventNumber);
	}

	// used forward and backward
	// resulting array is in descending order
	private async ValueTask<EventInfo[]> ReadEventInfoInternal<TStreamHandle>(
		TStreamHandle streamHandle,
		TFReaderLease reader,
		ReadIndexEntries<TStreamHandle> readIndexEntries,
		long startEventNumber,
		long endEventNumber,
		long beforePosition,
		bool deduplicate,
		CancellationToken token) {
		var entries = readIndexEntries(this, streamHandle, reader, startEventNumber, endEventNumber);
		var eventInfos = new List<EventInfo>();

		var prevEntry = new IndexEntry(long.MaxValue, long.MaxValue, long.MaxValue);

		// entries are from TableIndex.GetRange. They are in IndexEntry order descending.
		// we need to return EventInfos in EventNumber order descending. As we go we check if this
		// criteria is already met, and avoid the sorting and deduplicating linq query if it is.
		var sortAndDeduplicate = false;
		await foreach (var entry in entries.WithCancellation(token)) {
			if (entry.Position >= beforePosition)
				continue;

			if (prevEntry.Version <= entry.Version)
				sortAndDeduplicate = true;

			eventInfos.Add(new EventInfo(entry.Position, entry.Version));
			prevEntry = entry;
		}

		EventInfo[] result;
		if (sortAndDeduplicate) {
			if (deduplicate) {
				// note that even if _skipIndexScanOnRead = True, we're still reordering and filtering out duplicates here.
				result = eventInfos
					.OrderByDescending(x => x.EventNumber)
					.GroupBy(x => x.EventNumber)
					// keep the earliest event in the log (the index entries that were read are in descending order)
					.Select(x => x.Last())
					.ToArray();
			} else {
				result = eventInfos
					.OrderByDescending(x => x.EventNumber)
					.ToArray();
			}
		} else {
			result = eventInfos.ToArray();
		}

		return result;
	}

	public async ValueTask<TStreamId> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token) {
		Ensure.Nonnegative(transactionId);
		using var reader = _backend.BorrowReader();
		var res = await ReadPrepareInternal(reader, transactionId, token);
		return res is null ? default : res.EventStreamId;
	}

	public async ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(TStreamId streamId, CancellationToken token) {
		using var reader = _backend.BorrowReader();
		var sysSettings = _backend.GetSystemSettings() ?? SystemSettings.Default;
		StreamAcl acl;
		StreamAcl sysAcl;
		StreamAcl defAcl;
		var meta = await GetStreamMetadataCached(reader, streamId, token);
		if (await _systemStreams.IsSystemStream(streamId, token)) {
			defAcl = SystemSettings.Default.SystemStreamAcl;
			sysAcl = sysSettings.SystemStreamAcl ?? defAcl;
			acl = meta.Acl ?? sysAcl;
		} else {
			defAcl = SystemSettings.Default.UserStreamAcl;
			sysAcl = sysSettings.UserStreamAcl ?? defAcl;
			acl = meta.Acl ?? sysAcl;
		}

		return new(acl, sysAcl, defAcl);
	}

	async ValueTask<long> IIndexReader<TStreamId>.GetStreamLastEventNumber(TStreamId streamId, CancellationToken token) {
		Ensure.Valid(streamId, _validator);
		using var reader = _backend.BorrowReader();
		return await GetStreamLastEventNumberCached(reader, streamId, token);
	}

	public async ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, CancellationToken token) {
		Ensure.Valid(streamId, _validator);
		using var reader = _backend.BorrowReader();
		return await GetStreamLastEventNumber_KnownCollisions(streamId, beforePosition, reader, token);
	}

	private async ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, TFReaderLease reader, CancellationToken token) {
		return await _tableIndex.TryGetLatestEntry(streamId, beforePosition, IsForThisStream, token) is { } entry
			? entry.Version
			: ExpectedVersion.NoStream;

		async ValueTask<bool> IsForThisStream(IndexEntry indexEntry, CancellationToken token) {
			// we know that collisions have occurred for this stream's hash prior to "beforePosition" in the log
			// we just fetch the stream name from the log to make sure this index entry is for the correct stream
			var prepare = await ReadPrepareInternal(reader, indexEntry.Position, token);
			return StreamIdComparer.Equals(prepare.EventStreamId, streamId);
		}
	}

	// gets the last event number before beforePosition for the given stream hash. can assume that
	// the hash does not collide with anything before beforePosition.
	public async ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition, CancellationToken token) {
		using var reader = _backend.BorrowReader();
		return await GetStreamLastEventNumber_NoCollisions(stream, getStreamId, beforePosition, reader, token);
	}

	public TFReaderLease BorrowReader() => _backend.BorrowReader();

	private async ValueTask<long> GetStreamLastEventNumber_NoCollisions(
		ulong stream,
		Func<ulong, TStreamId> getStreamId,
		long beforePosition,
		TFReaderLease reader,
		CancellationToken token) {
		TStreamId streamId = default;

		return await _tableIndex.TryGetLatestEntry(stream, beforePosition, IsForThisStream, token) is { } entry
			? entry.Version
			: ExpectedVersion.NoStream;

		async ValueTask<bool> IsForThisStream(IndexEntry indexEntry, CancellationToken token) {
			// we know that there are no collisions for this hash prior to "beforePosition" in the log
			if (indexEntry.Position < beforePosition)
				return true;

			// fetch the correct stream name from the log if we haven't yet
			if (StreamIdComparer.Equals(streamId, default))
				streamId = getStreamId(stream);

			// compare the correct stream name against this index entry's stream name fetched from the log
			var prepare = await ReadPrepareInternal(reader, indexEntry.Position, token);
			return StreamIdComparer.Equals(prepare.EventStreamId, streamId);
		}
	}

	async ValueTask<StreamMetadata> IIndexReader<TStreamId>.GetStreamMetadata(TStreamId streamId, CancellationToken token) {
		Ensure.Valid(streamId, _validator);
		using var reader = _backend.BorrowReader();
		return await GetStreamMetadataCached(reader, streamId, token);
	}

	private async ValueTask<long> GetStreamLastEventNumberCached(TFReaderLease reader, TStreamId streamId, CancellationToken token) {
		// if this is metastream -- check if original stream was deleted, if yes -- metastream is deleted as well
		if (_systemStreams.IsMetaStream(streamId)
			&& await GetStreamLastEventNumberCached(reader, _systemStreams.OriginalStreamOf(streamId), token) ==
			EventNumber.DeletedStream)
			return EventNumber.DeletedStream;

		var cache = _backend.TryGetStreamLastEventNumber(streamId);
		if (cache.LastEventNumber != null) {
			Interlocked.Increment(ref _cachedStreamInfo);
			return cache.LastEventNumber.GetValueOrDefault();
		}

		Interlocked.Increment(ref _notCachedStreamInfo);
		var lastEventNumber = await GetStreamLastEventNumberUncached(reader, streamId, token);

		// Conditional update depending on previously returned cache info version.
		// If version is not correct -- nothing is changed in cache.
		// This update is conditioned to not interfere with updating stream cache info by commit procedure
		// (which is the source of truth).
		var res = _backend.UpdateStreamLastEventNumber(cache.Version, streamId, lastEventNumber);
		return res ?? lastEventNumber;
	}

	private async ValueTask<long> GetStreamLastEventNumberUncached(TFReaderLease reader, TStreamId streamId, CancellationToken token) {
		if (!_streamExistenceFilter.MightContain(streamId))
			return ExpectedVersion.NoStream;

		if (!_tableIndex.TryGetLatestEntry(streamId, out var latestEntry))
			return ExpectedVersion.NoStream;

		if (await ReadPrepareInternal(reader, latestEntry.Position, token) is not { } rec)
			throw new Exception($"Could not read latest stream's prepare for stream '{streamId}' at position {latestEntry.Position}");

		int count = 0;
		long startVersion = 0;
		long latestVersion = long.MinValue;
		if (StreamIdComparer.Equals(rec.EventStreamId, streamId)) {
			startVersion = Math.Max(latestEntry.Version, latestEntry.Version + 1);
			latestVersion = latestEntry.Version;
		}

		foreach (var indexEntry in _tableIndex.GetRange(streamId, startVersion, long.MaxValue,
					 limit: _hashCollisionReadLimit + 1)) {
			if (await ReadPrepareInternal(reader, indexEntry.Position, token) is { } r &&
				StreamIdComparer.Equals(r.EventStreamId, streamId)) {
				if (latestVersion is long.MinValue) {
					latestVersion = indexEntry.Version;
					continue;
				}

				return latestVersion < indexEntry.Version ? indexEntry.Version : latestVersion;
			}

			count++;
			Interlocked.Increment(ref _hashCollisions);
			if (count > _hashCollisionReadLimit) {
				Log.Error("A hash collision resulted in not finding the last event number for the stream {stream}.", streamId);
				return EventNumber.Invalid;
			}
		}

		return latestVersion == long.MinValue ? ExpectedVersion.NoStream : latestVersion;
	}

	private async ValueTask<bool?> OriginalStreamExists(TFReaderLease reader, TStreamId metaStreamId, CancellationToken token) {
		if (_systemStreams.IsMetaStream(metaStreamId)) {
			var originalStreamId = _systemStreams.OriginalStreamOf(metaStreamId);
			var lastEventNumber = await GetStreamLastEventNumberCached(reader, originalStreamId, token);
			return lastEventNumber is not ExpectedVersion.NoStream and not EventNumber.DeletedStream;
		}

		return null;
	}

	private async ValueTask<StreamMetadata> GetStreamMetadataCached(TFReaderLease reader, TStreamId streamId, CancellationToken token) {
		// if this is metastream -- check if original stream was deleted, if yes -- metastream is deleted as well
		if (_systemStreams.IsMetaStream(streamId))
			return _metastreamMetadata;

		var cache = _backend.TryGetStreamMetadata(streamId);
		if (cache.Metadata != null) {
			Interlocked.Increment(ref _cachedStreamInfo);
			return cache.Metadata;
		}

		Interlocked.Increment(ref _notCachedStreamInfo);
		var streamMetadata = await GetStreamMetadataUncached(reader, streamId, token);

		// Conditional update depending on previously returned cache info version.
		// If version is not correct -- nothing is changed in cache.
		// This update is conditioned to not interfere with updating stream cache info by commit procedure
		// (which is the source of truth).
		var res = _backend.UpdateStreamMetadata(cache.Version, streamId, streamMetadata);
		return res ?? streamMetadata;
	}

	private async ValueTask<StreamMetadata> GetStreamMetadataUncached(TFReaderLease reader, TStreamId streamId, CancellationToken token) {
		var metastreamId = _systemStreams.MetaStreamOf(streamId);
		var metaEventNumber = await GetStreamLastEventNumberCached(reader, metastreamId, token);
		if (metaEventNumber is ExpectedVersion.NoStream or EventNumber.DeletedStream)
			return StreamMetadata.Empty;

		if (await ReadPrepareInternal(reader, metastreamId, metaEventNumber, token) is not { } prepare)
			throw new Exception($"ReadPrepareInternal could not find metaevent #{metaEventNumber} on metastream '{metastreamId}'. " + "That should never happen.");

		if (prepare.Data.Length is 0 || prepare.Flags.HasNoneOf(PrepareFlags.IsJson))
			return StreamMetadata.Empty;

		var metadata = StreamMetadata.TryFromJsonBytes(prepare.Version, prepare.Data);
		return metadata;
	}

	private ValueTask<EventRecord> CreateEventRecord(long version, IPrepareLogRecord<TStreamId> prepare, string streamName, CancellationToken token) {
		return CreateEventRecord(version, prepare, streamName, _eventTypes, token);
	}

	private static async ValueTask<EventRecord> CreateEventRecord(long version, IPrepareLogRecord<TStreamId> prepare,
		string streamName, INameLookup<TStreamId> eventTypeLookup, CancellationToken token) {
		var eventTypeName = await eventTypeLookup.LookupName(prepare.EventType, token);

		return new(version, prepare, streamName, eventTypeName);
	}

	// This struct implements the IndexScanOnRead logic.
	//
	// The IndexScanOnRead logic deals with the case that there are duplicate
	// EventNumbers for a single stream (note: not talking about hash collisions), which
	// may also be out of order (possible when there are a mix of index table bitnesses)
	//
	// It deals with them by removing duplicates as they are added (preferring the record
	// with the lower LogPosition) and then sorting by EventNumber (descending) on output.
	//
	// If SkipIndexScanOnRead is true, then it is assumed that the EventRecords are added
	// (1) in EventNumber order descending and (2) without duplicate EventNumbers.
	public readonly struct ResultsDeduplicator {
		// when deduplicating, maps event number to index in _results
		private readonly Dictionary<long, int> _dict;
		private readonly List<EventRecord> _results;
		private readonly bool _skipIndexScanOnRead;
		// ReSharper disable once StaticMemberInGenericType
		private static readonly Comparison<EventRecord> ByEventNumberDesc = CompareByEventNumberDesc;

		public ResultsDeduplicator(int maxCount, bool skipIndexScanOnRead) {
			var capacity = Math.Min(maxCount, 256);
			_dict = skipIndexScanOnRead
				? null
				: new Dictionary<long, int>(capacity);
			_results = new(capacity);
			_skipIndexScanOnRead = skipIndexScanOnRead;
		}

		public int Count => _results.Count;

		public EventRecord[] ProduceArray() {
			var array = _results.ToArray();

			if (!_skipIndexScanOnRead) {
				// we already deduplicated on the way in, just sort here
				Array.Sort(array, ByEventNumberDesc);
			}

			return array;
		}

		public List<EventRecord> ProduceList() {
			var list = _results.ToList();

			if (!_skipIndexScanOnRead) {
				list.Sort(ByEventNumberDesc);
			}

			return list;
		}

		public void Add(EventRecord evt) {
			if (_skipIndexScanOnRead) {
				_results.Add(evt);
				return;
			}

			if (_dict.TryGetValue(evt.EventNumber, out var i)) {
				if (_results[i].LogPosition > evt.LogPosition) {
					_results[i] = evt;
				} else {
					// ignore
				}
			} else {
				_results.Add(evt);
				_dict[evt.EventNumber] = _results.Count - 1;
			}
		}

		private static int CompareByEventNumberDesc(EventRecord x, EventRecord y) {
			return y.EventNumber.CompareTo(x.EventNumber);
		}
	}
}
