using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Storage.ReaderIndex {
	public interface IIndexReader<TStreamId> {
		long CachedStreamInfo { get; }
		long NotCachedStreamInfo { get; }
		long HashCollisions { get; }

		// streamId drives the read, streamName is only for populating on the result.
		// this was less messy than safely adding the streamName to the EventRecord at some point after construction
		IndexReadEventResult ReadEvent(string streamName, TStreamId streamId, long eventNumber);
		IndexReadStreamResult ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount);
		IndexReadStreamResult ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount);
		StorageMessage.EffectiveAcl GetEffectiveAcl(TStreamId streamId);
		IndexReadEventInfoResult ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber);
		IndexReadEventInfoResult ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition);
		IndexReadEventInfoResult ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition);
		IndexReadEventInfoResult ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition);
		IndexReadEventInfoResult ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long fromEventNumber, int maxCount, long beforePosition);

		/// <summary>
		/// Doesn't filter $maxAge, $maxCount, $tb(truncate before), doesn't check stream deletion, etc.
		/// </summary>
		IPrepareLogRecord<TStreamId> ReadPrepare(TStreamId streamId, long eventNumber);

		TStreamId GetEventStreamIdByTransactionId(long transactionId);

		StreamMetadata GetStreamMetadata(TStreamId streamId);
		long GetStreamLastEventNumber(TStreamId streamId);
		long GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition);
		long GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition);
	}

	public abstract class IndexReader {
		public static string UnspecifiedStreamName { get; } = "Unspecified stream name";
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
		private int _hashCollisionReadLimit;

		public IndexReader(
			IIndexBackend<TStreamId> backend,
			ITableIndex<TStreamId> tableIndex,
			IStreamNamesProvider<TStreamId> streamNamesProvider,
			IValidator<TStreamId> validator,
			IExistenceFilterReader<TStreamId> streamExistenceFilter,
			StreamMetadata metastreamMetadata,
			int hashCollisionReadLimit, bool skipIndexScanOnRead) {
			Ensure.NotNull(backend, "backend");
			Ensure.NotNull(tableIndex, "tableIndex");
			Ensure.NotNull(streamNamesProvider, nameof(streamNamesProvider));
			Ensure.NotNull(validator, nameof(validator));
			Ensure.NotNull(metastreamMetadata, "metastreamMetadata");

			streamNamesProvider.SetReader(this);

			_backend = backend;
			_tableIndex = tableIndex;
			_systemStreams = streamNamesProvider.SystemStreams;
			_eventTypes = streamNamesProvider.EventTypes;
			_validator = validator;
			_streamExistenceFilter = streamExistenceFilter;
			_metastreamMetadata = metastreamMetadata;
			_hashCollisionReadLimit = hashCollisionReadLimit;
			_skipIndexScanOnRead = skipIndexScanOnRead;
		}

		IndexReadEventResult IIndexReader<TStreamId>.ReadEvent(string streamName, TStreamId streamId, long eventNumber) {
			Ensure.Valid(streamId, _validator);
			if (eventNumber < -1)
				throw new ArgumentOutOfRangeException("eventNumber");
			using (var reader = _backend.BorrowReader()) {
				return ReadEventInternal(reader, streamName, streamId, eventNumber);
			}
		}

		private IndexReadEventResult ReadEventInternal(TFReaderLease reader, string streamName, TStreamId streamId, long eventNumber) {
			var lastEventNumber = GetStreamLastEventNumberCached(reader, streamId);
			var metadata = GetStreamMetadataCached(reader, streamId);
			var originalStreamExists = OriginalStreamExists(reader, streamId);
			if (lastEventNumber == EventNumber.DeletedStream)
				return new IndexReadEventResult(ReadEventResult.StreamDeleted, metadata, lastEventNumber,
					originalStreamExists);
			if (lastEventNumber == ExpectedVersion.NoStream || metadata.TruncateBefore == EventNumber.DeletedStream)
				return new IndexReadEventResult(ReadEventResult.NoStream, metadata, lastEventNumber,
					originalStreamExists);
			if (lastEventNumber == EventNumber.Invalid)
				return new IndexReadEventResult(ReadEventResult.NoStream, metadata, lastEventNumber,
					originalStreamExists);

			if (eventNumber == -1)
				eventNumber = lastEventNumber;

			long minEventNumber = 0;
			if (metadata.MaxCount.HasValue)
				minEventNumber = Math.Max(minEventNumber, lastEventNumber - metadata.MaxCount.Value + 1);
			if (metadata.TruncateBefore.HasValue)
				minEventNumber = Math.Max(minEventNumber, metadata.TruncateBefore.Value);
			//TODO(clc): confirm this logic, it seems that reads less than min should be invaild rather than found
			if (eventNumber < minEventNumber || eventNumber > lastEventNumber)
				return new IndexReadEventResult(ReadEventResult.NotFound, metadata, lastEventNumber,
					originalStreamExists);

			var prepare = ReadPrepareInternal(reader, streamId, eventNumber);
			if (prepare != null) {
				if (metadata.MaxAge.HasValue && prepare.TimeStamp < DateTime.UtcNow - metadata.MaxAge.Value)
					return new IndexReadEventResult(ReadEventResult.NotFound, metadata, lastEventNumber,
						originalStreamExists);
				return new IndexReadEventResult(ReadEventResult.Success, CreateEventRecord(eventNumber, prepare, streamName),
					metadata, lastEventNumber, originalStreamExists);
			}

			return new IndexReadEventResult(ReadEventResult.NotFound, metadata, lastEventNumber,
				originalStreamExists: originalStreamExists);
		}

		IPrepareLogRecord<TStreamId> IIndexReader<TStreamId>.ReadPrepare(TStreamId streamId, long eventNumber) {
			using (var reader = _backend.BorrowReader()) {
				return ReadPrepareInternal(reader, streamId, eventNumber);
			}
		}

		private IPrepareLogRecord<TStreamId> ReadPrepareInternal(TFReaderLease reader, TStreamId streamId, long eventNumber) {
			// we assume that you already did check for stream deletion
			Ensure.Valid(streamId, _validator);
			Ensure.Nonnegative(eventNumber, "eventNumber");

			return _skipIndexScanOnRead
				? ReadPrepareSkipScan(reader, streamId, eventNumber)
				: ReadPrepare(reader, streamId, eventNumber);
		}

		private IPrepareLogRecord<TStreamId> ReadPrepare(TFReaderLease reader, TStreamId streamId, long eventNumber) {
			var recordsQuery = _tableIndex.GetRange(streamId, eventNumber, eventNumber)
				.Select(x => new { x.Version, Prepare = ReadPrepareInternal(reader, x.Position) })
				.Where(x => x.Prepare != null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamId))
				.GroupBy(x => x.Version).Select(x => x.Last()).ToList();
			if (recordsQuery.Count() == 1) {
				return recordsQuery.First().Prepare;
			}

			return null;
		}

		private IPrepareLogRecord<TStreamId> ReadPrepareSkipScan(TFReaderLease reader, TStreamId streamId, long eventNumber) {
			long position;
			if (_tableIndex.TryGetOneValue(streamId, eventNumber, out position)) {
				var rec = ReadPrepareInternal(reader, position);
				if (rec != null && StreamIdComparer.Equals(rec.EventStreamId, streamId))
					return rec;

				foreach (var indexEntry in _tableIndex.GetRange(streamId, eventNumber, eventNumber)) {
					Interlocked.Increment(ref _hashCollisions);
					if (indexEntry.Position == position)
						continue;
					rec = ReadPrepareInternal(reader, indexEntry.Position);
					if (rec != null && StreamIdComparer.Equals(rec.EventStreamId, streamId))
						return rec;
				}
			}

			return null;
		}

		protected static IPrepareLogRecord<TStreamId> ReadPrepareInternal(TFReaderLease reader, long logPosition) {
			RecordReadResult result = reader.TryReadAt(logPosition, couldBeScavenged: true);
			if (!result.Success)
				return null;

			if (result.LogRecord.RecordType != LogRecordType.Prepare &&
				result.LogRecord.RecordType != LogRecordType.Stream &&
				result.LogRecord.RecordType != LogRecordType.EventType)
				throw new Exception(string.Format("Incorrect type of log record {0}, expected Prepare record.",
					result.LogRecord.RecordType));
			return (IPrepareLogRecord<TStreamId>)result.LogRecord;
		}

		IndexReadStreamResult IIndexReader<TStreamId>.
			ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount) {
			return ReadStreamEventsForwardInternal(streamName, streamId, fromEventNumber, maxCount, _skipIndexScanOnRead);
		}

		private IndexReadStreamResult ReadStreamEventsForwardInternal(string streamName, TStreamId streamId, long fromEventNumber,
			int maxCount, bool skipIndexScanOnRead) {
			Ensure.Valid(streamId, _validator);
			Ensure.Nonnegative(fromEventNumber, "fromEventNumber");
			Ensure.Positive(maxCount, "maxCount");

			using (var reader = _backend.BorrowReader()) {
				var lastEventNumber = GetStreamLastEventNumberCached(reader, streamId);
				var metadata = GetStreamMetadataCached(reader, streamId);
				if (lastEventNumber == EventNumber.DeletedStream)
					return new IndexReadStreamResult(fromEventNumber, maxCount, ReadStreamResult.StreamDeleted,
						StreamMetadata.Empty, lastEventNumber);
				if (lastEventNumber == ExpectedVersion.NoStream || metadata.TruncateBefore == EventNumber.DeletedStream)
					return new IndexReadStreamResult(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata,
						lastEventNumber);
				if (lastEventNumber == EventNumber.Invalid)
					return new IndexReadStreamResult(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata,
						lastEventNumber);

				long startEventNumber = fromEventNumber;
				long endEventNumber = fromEventNumber < long.MaxValue - maxCount + 1 ?
					fromEventNumber + maxCount - 1 : long.MaxValue;

				// calculate minEventNumber, which is the lower bound that we are allowed to read
				// according to the MaxCount and TruncateBefore
				long minEventNumber = 0;
				if (metadata.MaxCount.HasValue)
					minEventNumber = Math.Max(minEventNumber, lastEventNumber - metadata.MaxCount.Value + 1);
				if (metadata.TruncateBefore.HasValue)
					minEventNumber = Math.Max(minEventNumber, metadata.TruncateBefore.Value);

				// early return if we are trying to read events that are all below the lower bound
				if (endEventNumber < minEventNumber)
					return new IndexReadStreamResult(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
						metadata, minEventNumber, lastEventNumber, isEndOfStream: false);

				// start our read at the lower bound if we were going to start it before hand.
				startEventNumber = Math.Max(startEventNumber, minEventNumber);

				// early return if we are trying to read events that are all above the upper bound
				if (startEventNumber > lastEventNumber)
					return new IndexReadStreamResult(
						fromEventNumber: fromEventNumber,
						maxCount: maxCount,
						records: IndexReadStreamResult.EmptyRecords,
						metadata: metadata,
						nextEventNumber: lastEventNumber + 1,
						lastEventNumber: lastEventNumber,
						isEndOfStream: true);

				if (metadata.MaxAge.HasValue) {
					return ForStreamWithMaxAge(streamId, streamName,
						fromEventNumber, maxCount,
						startEventNumber, endEventNumber, lastEventNumber,
						metadata.MaxAge.Value, metadata, _tableIndex, reader, _eventTypes,
						skipIndexScanOnRead);
				}
				var recordsQuery = _tableIndex.GetRange(streamId, startEventNumber, endEventNumber)
					.Select(x => new { x.Version, Prepare = ReadPrepareInternal(reader, x.Position) })
					.Where(x => x.Prepare != null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamId));
				if (!skipIndexScanOnRead) {
					recordsQuery = recordsQuery.OrderByDescending(x => x.Version)
						.GroupBy(x => x.Version).Select(x => x.Last());
				}

				var records = recordsQuery.Reverse().Select(x => CreateEventRecord(x.Version, x.Prepare, streamName)).ToArray();

				long nextEventNumber = Math.Min(endEventNumber + 1, lastEventNumber + 1);
				if (records.Length > 0)
					nextEventNumber = records[records.Length - 1].EventNumber + 1;
				var isEndOfStream = endEventNumber >= lastEventNumber;
				return new IndexReadStreamResult(fromEventNumber, maxCount, records, metadata,
					nextEventNumber, lastEventNumber, isEndOfStream);
			}

			IndexReadStreamResult ForStreamWithMaxAge(TStreamId streamId,
				string streamName,
				long fromEventNumber, int maxCount, long startEventNumber,
				long endEventNumber, long lastEventNumber, TimeSpan maxAge, StreamMetadata metadata,
				ITableIndex<TStreamId> tableIndex, TFReaderLease reader, INameLookup<TStreamId> eventTypes,
				bool skipIndexScanOnRead) {

				if (startEventNumber > lastEventNumber) {
					return new IndexReadStreamResult(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
						metadata, lastEventNumber + 1, lastEventNumber, isEndOfStream: true);
				}

				var ageThreshold = DateTime.UtcNow - maxAge;
				var nextEventNumber = lastEventNumber;
				var indexEntries = tableIndex.GetRange(streamId, startEventNumber, endEventNumber);

				//Move to the first valid entries. At this point we could instead return an empty result set with the minimum set, but that would 
				//involve an additional set of reads for no good reason
				while (indexEntries.Count == 0) {
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
						return new IndexReadStreamResult(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
							metadata, lastEventNumber + 1, lastEventNumber, isEndOfStream: false);
					}
				}

				var results = new ResultsDeduplicator(maxCount, skipIndexScanOnRead);

				for (int i = 0; i < indexEntries.Count; i++) {
					var prepare = ReadPrepareInternal(reader, indexEntries[i].Position);

					if (prepare == null || !StreamIdComparer.Equals(prepare.EventStreamId, streamId)) {
						continue;
					}

					if (prepare?.TimeStamp >= ageThreshold) {
						results.Add(CreateEventRecord(indexEntries[i].Version, prepare, streamName, eventTypes));
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
					return new IndexReadStreamResult(fromEventNumber, maxCount, resultsArray, metadata,
						nextEventNumber, lastEventNumber, isEndOfStream);
				}

				//we didn't find anything valid yet, now we need to search
				//the entries we found were all either scavenged, or expired, or for another stream.

				//check high value will be valid, otherwise early return.
				// this resolves hash collisions itself
				var lastEvent = ReadPrepareInternal(reader, streamId, eventNumber: lastEventNumber);
				if (lastEvent == null || lastEvent.TimeStamp < ageThreshold || lastEventNumber < fromEventNumber) {
					//No events in the stream are < max age, so return an empty set
					return new IndexReadStreamResult(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
						metadata, lastEventNumber + 1, lastEventNumber, isEndOfStream: true);
				}

				// intuition for loop termination here is that the explicit continue cases are
				// the only way to continue the iteration. apart from those it return;s or break;s.
				// the continue cases always narrow the gap between low and high
				//
				// low, mid, and high are event numbers (versions)
				// attempt to initialize low to the latest (highest) entry that we found but in the unlikely event that
				// they're out of version order thats still ok, low will just be lower than it could have been.
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
					var (lowPrepareVersion, lowPrepare) = LowPrepare(reader, indexEntries, streamId);
					if (lowPrepare?.TimeStamp >= ageThreshold) {
						// found a lowPrepare for this stream. it has not expired. chop lower in case there are more
						high = mid - 1;
						nextEventNumber = lowPrepareVersion;
						continue;
					}

					var (highPrepareVersion, highPrepare) = HighPrepare(reader, indexEntries, streamId);
					if (highPrepare?.TimeStamp < ageThreshold) {
						// found a highPrepare for this stream. it has expired. chop higher
						low = highPrepareVersion + 1;
						continue;
					}

					//ok, some entries must match, if not (due to time moving forwards) we can just reissue based on the current mid
					// also might have no matches because the getrange call returned addresses that
					// were all scavenged or for other streams, in which case we won't add anything to results here
					for (int i = 0; i < indexEntries.Count; i++) {
						var prepare = ReadPrepareInternal(reader, indexEntries[i].Position);

						if (prepare == null || !StreamIdComparer.Equals(prepare.EventStreamId, streamId))
							continue;

						if (prepare?.TimeStamp >= ageThreshold) {
							results.Add(CreateEventRecord(indexEntries[i].Version, prepare, streamName, eventTypes));
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

						return new IndexReadStreamResult(fromEventNumber, maxCount, resultsList.ToArray(), metadata,
							nextEventNumber, lastEventNumber, isEndOfStream);
					}

					break;
				}

				//We didn't find anything, send back to the client with the latest position to retry
				return new IndexReadStreamResult(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
					metadata, nextEventNumber, lastEventNumber, isEndOfStream: false);

				[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
				static (long, IPrepareLogRecord<TStreamId>) LowPrepare(
					TFReaderLease tfReaderLease,
					IReadOnlyList<IndexEntry> entries,
					TStreamId streamId) {

					for (int i = entries.Count - 1; i >= 0; i--) {
						var prepare = ReadPrepareInternal(tfReaderLease, entries[i].Position);
						if (prepare != null && StreamIdComparer.Equals(prepare.EventStreamId, streamId))
							return (entries[i].Version, prepare);
					}

					return (default, null);
				}

				[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
				static (long, IPrepareLogRecord<TStreamId>) HighPrepare(
					TFReaderLease tfReaderLease,
					IReadOnlyList<IndexEntry> entries,
					TStreamId streamId) {

					for (int i = 0; i < entries.Count; i++) {
						var prepare = ReadPrepareInternal(tfReaderLease, entries[i].Position);
						if (prepare != null && StreamIdComparer.Equals(prepare.EventStreamId, streamId))
							return (entries[i].Version, prepare);
					}

					return (default, null);
				}
			}
		}

		delegate IEnumerable<IndexEntry> ReadIndexEntries<TStreamHandle>(
			IndexReader<TStreamId> indexReader,
			TStreamHandle streamHandle,
			TFReaderLease reader,
			long startEventNumber,
			long endEventNumber);

		private static ReadIndexEntries<TStreamId> ReadIndexEntries_RemoveCollisions =
			(indexReader, streamHandle, reader, startEventNumber, endEventNumber) =>
				indexReader._tableIndex.GetRange(streamHandle, startEventNumber, endEventNumber)
					.Select(x => new { IndexEntry = x, Prepare = ReadPrepareInternal(reader, x.Position) })
					.Where(x => x.Prepare != null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamHandle))
					.Select(x => x.IndexEntry);

		private static ReadIndexEntries<ulong> ReadIndexEntries_NoCollisions =
			(indexReader, streamHandle, reader, startEventNumber, endEventNumber) =>
				indexReader._tableIndex.GetRange(streamHandle, startEventNumber, endEventNumber);

		public IndexReadEventInfoResult ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber) {
			using (var reader = _backend.BorrowReader()) {
				var result = ReadEventInfoForwardInternal(
					streamId,
					reader,
					ReadIndexEntries_RemoveCollisions,
					// the next event number doesn't matter in this context since we're reading a single event without a specific direction
					getNextEventNumber: static (_, _, _) => -1,
					fromEventNumber: eventNumber,
					maxCount: 1,
					beforePosition: long.MaxValue,
					deduplicate: false);

				// ensure that the next event number is set to -1
				return new IndexReadEventInfoResult(result.EventInfos, nextEventNumber: -1);
			}
		}

		// note for simplicity skipIndexScanOnRead is always treated as false. see ReadEventInfoInternal
		public IndexReadEventInfoResult ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition) {
			using (var reader = _backend.BorrowReader()) {
				return ReadEventInfoForwardInternal(
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
					deduplicate: true);
			}
		}

		// note for simplicity skipIndexScanOnRead is always treated as false. see ReadEventInfoInternal
		public IndexReadEventInfoResult ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition) {
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
				deduplicate: true);
		}

		private IndexReadEventInfoResult ReadEventInfoForwardInternal<TStreamHandle>(
			TStreamHandle streamHandle,
			TFReaderLease reader,
			ReadIndexEntries<TStreamHandle> readIndexEntries,
			Func<IndexReader<TStreamId>, TStreamHandle, long, long> getNextEventNumber,
			long fromEventNumber,
			int maxCount,
			long beforePosition,
			bool deduplicate) {

			Ensure.Nonnegative(fromEventNumber, nameof(fromEventNumber));
			Ensure.Positive(maxCount, nameof(maxCount));

			var startEventNumber = fromEventNumber;
			var endEventNumber = fromEventNumber > long.MaxValue - maxCount + 1 ?
				long.MaxValue : fromEventNumber + maxCount - 1;

			var eventInfos = ReadEventInfoInternal(streamHandle, reader, readIndexEntries, startEventNumber,
				endEventNumber, beforePosition, deduplicate);
			Array.Reverse(eventInfos);

			long nextEventNumber;
			if (endEventNumber >= long.MaxValue)
				nextEventNumber = -1;
			else if (eventInfos.Length == 0)
				nextEventNumber = getNextEventNumber(this, streamHandle, endEventNumber);
			else
				nextEventNumber = endEventNumber + 1;

			return new IndexReadEventInfoResult(eventInfos, nextEventNumber);
		}

		IndexReadStreamResult IIndexReader<TStreamId>.
			ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount) {
			return ReadStreamEventsBackwardInternal(streamName, streamId, fromEventNumber, maxCount, _skipIndexScanOnRead);
		}

		private IndexReadStreamResult ReadStreamEventsBackwardInternal(string streamName, TStreamId streamId, long fromEventNumber,
			int maxCount, bool skipIndexScanOnRead) {
			Ensure.Valid(streamId, _validator);
			Ensure.Positive(maxCount, "maxCount");

			using (var reader = _backend.BorrowReader()) {
				var lastEventNumber = GetStreamLastEventNumberCached(reader, streamId);
				var metadata = GetStreamMetadataCached(reader, streamId);
				if (lastEventNumber == EventNumber.DeletedStream)
					return new IndexReadStreamResult(fromEventNumber, maxCount, ReadStreamResult.StreamDeleted,
						StreamMetadata.Empty, lastEventNumber);
				if (lastEventNumber == ExpectedVersion.NoStream || metadata.TruncateBefore == EventNumber.DeletedStream)
					return new IndexReadStreamResult(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata,
						lastEventNumber);
				if (lastEventNumber == EventNumber.Invalid)
					return new IndexReadStreamResult(fromEventNumber, maxCount, ReadStreamResult.NoStream, metadata,
						lastEventNumber);

				long endEventNumber = fromEventNumber < 0 ? lastEventNumber : fromEventNumber;
				long startEventNumber = Math.Max(0L, endEventNumber - maxCount + 1);
				bool isEndOfStream = false;

				long minEventNumber = 0;
				if (metadata.MaxCount.HasValue)
					minEventNumber = Math.Max(minEventNumber, lastEventNumber - metadata.MaxCount.Value + 1);
				if (metadata.TruncateBefore.HasValue)
					minEventNumber = Math.Max(minEventNumber, metadata.TruncateBefore.Value);
				if (endEventNumber < minEventNumber)
					return new IndexReadStreamResult(fromEventNumber, maxCount, IndexReadStreamResult.EmptyRecords,
						metadata, -1, lastEventNumber, isEndOfStream: true);

				if (startEventNumber <= minEventNumber) {
					isEndOfStream = true;
					startEventNumber = minEventNumber;
				}

				var recordsQuery = _tableIndex.GetRange(streamId, startEventNumber, endEventNumber)
					.Select(x => new { x.Version, Prepare = ReadPrepareInternal(reader, x.Position) })
					.Where(x => x.Prepare != null && StreamIdComparer.Equals(x.Prepare.EventStreamId, streamId));
				if (!skipIndexScanOnRead) {
					recordsQuery = recordsQuery.OrderByDescending(x => x.Version)
						.GroupBy(x => x.Version).Select(x => x.Last());
					;
				}

				if (metadata.MaxAge.HasValue) {
					var ageThreshold = DateTime.UtcNow - metadata.MaxAge.Value;
					recordsQuery = recordsQuery.Where(x => x.Prepare.TimeStamp >= ageThreshold);
				}

				var records = recordsQuery.Select(x => CreateEventRecord(x.Version, x.Prepare, streamName)).ToArray();

				isEndOfStream = isEndOfStream
								|| startEventNumber == 0
								|| (startEventNumber <= lastEventNumber
									&& (records.Length == 0 ||
										records[records.Length - 1].EventNumber != startEventNumber));
				long nextEventNumber = isEndOfStream ? -1 : Math.Min(startEventNumber - 1, lastEventNumber);
				return new IndexReadStreamResult(endEventNumber, maxCount, records, metadata,
					nextEventNumber, lastEventNumber, isEndOfStream);
			}
		}

		public IndexReadEventInfoResult ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
			long beforePosition) {
			if (fromEventNumber < 0)
				fromEventNumber = GetStreamLastEventNumber_KnownCollisions(streamId, beforePosition);

			if (fromEventNumber == ExpectedVersion.NoStream)
				return new IndexReadEventInfoResult(new EventInfo[] { }, -1);

			using (var reader = _backend.BorrowReader()) {
				return ReadEventInfoBackwardInternal(
					streamId,
					reader,
					ReadIndexEntries_RemoveCollisions,
					static (self, streamHandle, beforeEventNumber) => {
						if (!self._tableIndex.TryGetPreviousEntry(streamHandle, beforeEventNumber, out var entry))
							return -1;

						// Note that this event number may be for a colliding stream. It is not a major issue since these
						// colliding events will be filtered out during the next read. However, it may cause some extra empty reads.
						return entry.Version;
					},
					fromEventNumber,
					maxCount,
					beforePosition,
					deduplicate: true);
			}
		}

		public IndexReadEventInfoResult ReadEventInfoBackward_NoCollisions(
			ulong stream,
			Func<ulong, TStreamId> getStreamId,
			long fromEventNumber,
			int maxCount,
			long beforePosition) {

			if (fromEventNumber < 0)
				fromEventNumber = GetStreamLastEventNumber_NoCollisions(stream, getStreamId, beforePosition);

			if (fromEventNumber == ExpectedVersion.NoStream)
				return new IndexReadEventInfoResult(new EventInfo[] { }, -1);

			return ReadEventInfoBackwardInternal(
				stream,
				default,
				ReadIndexEntries_NoCollisions,
				static (self, streamHandle, beforeEventNumber) => {
					if (!self._tableIndex.TryGetPreviousEntry(streamHandle, beforeEventNumber, out var entry))
						return -1;
					return entry.Version;
				},
				fromEventNumber,
				maxCount,
				beforePosition,
				deduplicate: true);
		}

		private IndexReadEventInfoResult ReadEventInfoBackwardInternal<TStreamHandle>(
			TStreamHandle streamHandle,
			TFReaderLease reader,
			ReadIndexEntries<TStreamHandle> readIndexEntries,
			Func<IndexReader<TStreamId>, TStreamHandle, long, long> getNextEventNumber,
			long fromEventNumber,
			int maxCount,
			long beforePosition,
			bool deduplicate) {

			Ensure.Nonnegative(fromEventNumber, nameof(fromEventNumber));
			Ensure.Positive(maxCount, nameof(maxCount));

			var startEventNumber = Math.Max(0L, fromEventNumber - maxCount + 1);
			var endEventNumber = fromEventNumber;

			var eventInfos = ReadEventInfoInternal(streamHandle, reader, readIndexEntries, startEventNumber,
				endEventNumber, beforePosition, deduplicate);

			long nextEventNumber;
			if (startEventNumber <= 0)
				nextEventNumber = -1;
			else if (eventInfos.Length == 0)
				nextEventNumber = getNextEventNumber(this, streamHandle, startEventNumber);
			else
				nextEventNumber = startEventNumber - 1;

			return new IndexReadEventInfoResult(eventInfos, nextEventNumber);
		}

		// used forward and backward
		// resulting array is in descending order
		private EventInfo[] ReadEventInfoInternal<TStreamHandle>(
			TStreamHandle streamHandle,
			TFReaderLease reader,
			ReadIndexEntries<TStreamHandle> readIndexEntries,
			long startEventNumber,
			long endEventNumber,
			long beforePosition,
			bool deduplicate) {

			var entries = readIndexEntries(this, streamHandle, reader, startEventNumber, endEventNumber);
			var eventInfos = new List<EventInfo>();

			var prevEntry = new IndexEntry(long.MaxValue, long.MaxValue, long.MaxValue);

			// entries are from TableIndex.GetRange. They are in IndexEntry order descending.
			// we need to return EventInfos in EventNumber order descending. As we go we check if this
			// criteria is already met, and avoid the sorting and deduplicating linq query if it is.
			var sortAndDeduplicate = false;
			foreach (var entry in entries) {
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

		public TStreamId GetEventStreamIdByTransactionId(long transactionId) {
			Ensure.Nonnegative(transactionId, "transactionId");
			using (var reader = _backend.BorrowReader()) {
				var res = ReadPrepareInternal(reader, transactionId);
				return res == null ? default : res.EventStreamId;
			}
		}

		public StorageMessage.EffectiveAcl GetEffectiveAcl(TStreamId streamId) {
			using (var reader = _backend.BorrowReader()) {
				var sysSettings = _backend.GetSystemSettings() ?? SystemSettings.Default;
				StreamAcl acl;
				StreamAcl sysAcl;
				StreamAcl defAcl;
				var meta = GetStreamMetadataCached(reader, streamId);
				if (_systemStreams.IsSystemStream(streamId)) {
					defAcl = SystemSettings.Default.SystemStreamAcl;
					sysAcl = sysSettings.SystemStreamAcl ?? defAcl;
					acl = meta.Acl ?? sysAcl;
				} else {
					defAcl = SystemSettings.Default.UserStreamAcl;
					sysAcl = sysSettings.UserStreamAcl ?? defAcl;
					acl = meta.Acl ?? sysAcl;
				}
				return new StorageMessage.EffectiveAcl(acl, sysAcl, defAcl);
			}
		}

		long IIndexReader<TStreamId>.GetStreamLastEventNumber(TStreamId streamId) {
			Ensure.Valid(streamId, _validator);
			using (var reader = _backend.BorrowReader()) {
				return GetStreamLastEventNumberCached(reader, streamId);
			}
		}

		public long GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition) {
			Ensure.Valid(streamId, _validator);
			using (var reader = _backend.BorrowReader()) {
				return GetStreamLastEventNumber_KnownCollisions(streamId, beforePosition, reader);
			}
		}

		private long GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, TFReaderLease reader) {
			bool IsForThisStream(IndexEntry indexEntry) {
				// we know that collisions have occurred for this stream's hash prior to "beforePosition" in the log
				// we just fetch the stream name from the log to make sure this index entry is for the correct stream
				var prepare = ReadPrepareInternal(reader, indexEntry.Position);
				return StreamIdComparer.Equals(prepare.EventStreamId, streamId);
			}

			if (!_tableIndex.TryGetLatestEntry(streamId, beforePosition, IsForThisStream, out var entry))
				return ExpectedVersion.NoStream;

			return entry.Version;
		}

		// gets the last event number before beforePosition for the given stream hash. can assume that
		// the hash does not collide with anything before beforePosition.
		public long GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition) {
			using (var reader = _backend.BorrowReader()) {
				return GetStreamLastEventNumber_NoCollisions(stream, getStreamId, beforePosition, reader);
			}
		}

		private long GetStreamLastEventNumber_NoCollisions(
			ulong stream,
			Func<ulong, TStreamId> getStreamId,
			long beforePosition,
			TFReaderLease reader) {

			TStreamId streamId = default;

			bool IsForThisStream(IndexEntry indexEntry) {
				// we know that there are no collisions for this hash prior to "beforePosition" in the log
				if (indexEntry.Position < beforePosition)
					return true;

				// fetch the correct stream name from the log if we haven't yet
				if (StreamIdComparer.Equals(streamId, default))
					streamId = getStreamId(stream);

				// compare the correct stream name against this index entry's stream name fetched from the log
				var prepare = ReadPrepareInternal(reader, indexEntry.Position);
				return StreamIdComparer.Equals(prepare.EventStreamId, streamId);
			}

			if (!_tableIndex.TryGetLatestEntry(stream, beforePosition, IsForThisStream, out var entry))
				return ExpectedVersion.NoStream;

			return entry.Version;
		}

		StreamMetadata IIndexReader<TStreamId>.GetStreamMetadata(TStreamId streamId) {
			Ensure.Valid(streamId, _validator);
			using (var reader = _backend.BorrowReader()) {
				return GetStreamMetadataCached(reader, streamId);
			}
		}

		private long GetStreamLastEventNumberCached(TFReaderLease reader, TStreamId streamId) {
			// if this is metastream -- check if original stream was deleted, if yes -- metastream is deleted as well
			if (_systemStreams.IsMetaStream(streamId)
				&& GetStreamLastEventNumberCached(reader, _systemStreams.OriginalStreamOf(streamId)) ==
				EventNumber.DeletedStream)
				return EventNumber.DeletedStream;

			var cache = _backend.TryGetStreamLastEventNumber(streamId);
			if (cache.LastEventNumber != null) {
				Interlocked.Increment(ref _cachedStreamInfo);
				return cache.LastEventNumber.GetValueOrDefault();
			}

			Interlocked.Increment(ref _notCachedStreamInfo);
			var lastEventNumber = GetStreamLastEventNumberUncached(reader, streamId);

			// Conditional update depending on previously returned cache info version.
			// If version is not correct -- nothing is changed in cache.
			// This update is conditioned to not interfere with updating stream cache info by commit procedure
			// (which is the source of truth).
			var res = _backend.UpdateStreamLastEventNumber(cache.Version, streamId, lastEventNumber);
			return res ?? lastEventNumber;
		}

		private long GetStreamLastEventNumberUncached(TFReaderLease reader, TStreamId streamId) {
			if (!_streamExistenceFilter.MightContain(streamId))
				return ExpectedVersion.NoStream;

			IndexEntry latestEntry;
			if (!_tableIndex.TryGetLatestEntry(streamId, out latestEntry))
				return ExpectedVersion.NoStream;

			var rec = ReadPrepareInternal(reader, latestEntry.Position);
			if (rec == null)
				throw new Exception(
					$"Could not read latest stream's prepare for stream '{streamId}' at position {latestEntry.Position}");

			int count = 0;
			long startVersion = 0;
			long latestVersion = long.MinValue;
			if (StreamIdComparer.Equals(rec.EventStreamId, streamId)) {
				startVersion = Math.Max(latestEntry.Version, latestEntry.Version + 1);
				latestVersion = latestEntry.Version;
			}

			foreach (var indexEntry in _tableIndex.GetRange(streamId, startVersion, long.MaxValue,
				limit: _hashCollisionReadLimit + 1)) {
				var r = ReadPrepareInternal(reader, indexEntry.Position);
				if (r != null && StreamIdComparer.Equals(r.EventStreamId, streamId)) {
					if (latestVersion == long.MinValue) {
						latestVersion = indexEntry.Version;
						continue;
					}

					return latestVersion < indexEntry.Version ? indexEntry.Version : latestVersion;
				}

				count++;
				Interlocked.Increment(ref _hashCollisions);
				if (count > _hashCollisionReadLimit) {
					Log.Error("A hash collision resulted in not finding the last event number for the stream {stream}.",
						streamId);
					return EventNumber.Invalid;
				}
			}

			return latestVersion == long.MinValue ? ExpectedVersion.NoStream : latestVersion;
		}

		private bool? OriginalStreamExists(TFReaderLease reader, TStreamId metaStreamId) {
			if (_systemStreams.IsMetaStream(metaStreamId)) {
				var originalStreamId = _systemStreams.OriginalStreamOf(metaStreamId);
				var lastEventNumber = GetStreamLastEventNumberCached(reader, originalStreamId);
				if (lastEventNumber == ExpectedVersion.NoStream || lastEventNumber == EventNumber.DeletedStream)
					return false;
				return true;
			}

			return null;
		}

		private StreamMetadata GetStreamMetadataCached(TFReaderLease reader, TStreamId streamId) {
			// if this is metastream -- check if original stream was deleted, if yes -- metastream is deleted as well
			if (_systemStreams.IsMetaStream(streamId))
				return _metastreamMetadata;

			var cache = _backend.TryGetStreamMetadata(streamId);
			if (cache.Metadata != null) {
				Interlocked.Increment(ref _cachedStreamInfo);
				return cache.Metadata;
			}

			Interlocked.Increment(ref _notCachedStreamInfo);
			var streamMetadata = GetStreamMetadataUncached(reader, streamId);

			// Conditional update depending on previously returned cache info version.
			// If version is not correct -- nothing is changed in cache.
			// This update is conditioned to not interfere with updating stream cache info by commit procedure
			// (which is the source of truth).
			var res = _backend.UpdateStreamMetadata(cache.Version, streamId, streamMetadata);
			return res ?? streamMetadata;
		}

		private StreamMetadata GetStreamMetadataUncached(TFReaderLease reader, TStreamId streamId) {
			var metastreamId = _systemStreams.MetaStreamOf(streamId);
			var metaEventNumber = GetStreamLastEventNumberCached(reader, metastreamId);
			if (metaEventNumber == ExpectedVersion.NoStream || metaEventNumber == EventNumber.DeletedStream)
				return StreamMetadata.Empty;

			IPrepareLogRecord prepare = ReadPrepareInternal(reader, metastreamId, metaEventNumber);
			if (prepare == null)
				throw new Exception(string.Format(
					"ReadPrepareInternal could not find metaevent #{0} on metastream '{1}'. "
					+ "That should never happen.", metaEventNumber, metastreamId));

			if (prepare.Data.Length == 0 || prepare.Flags.HasNoneOf(PrepareFlags.IsJson))
				return StreamMetadata.Empty;

			var metadata = StreamMetadata.TryFromJsonBytes(prepare.Version, prepare.Data);
			return metadata;
		}

		private EventRecord CreateEventRecord(long version, IPrepareLogRecord<TStreamId> prepare, string streamName) {
			return CreateEventRecord(version, prepare, streamName, _eventTypes);
		}

		private static EventRecord CreateEventRecord(long version, IPrepareLogRecord<TStreamId> prepare,
			string streamName, INameLookup<TStreamId> eventTypeLookup) {

			var eventTypeName = eventTypeLookup.LookupName(prepare.EventType);

			return new EventRecord(version, prepare, streamName, eventTypeName);
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
			private static readonly Comparison<EventRecord> _byEventNumberDesc = CompareByEventNumberDesc;

			public ResultsDeduplicator(int maxCount, bool skipIndexScanOnRead) {
				var capacity = Math.Min(maxCount, 256);
				_dict = skipIndexScanOnRead
					? null
					: new Dictionary<long, int>(capacity);
				_results = new List<EventRecord>(capacity);
				_skipIndexScanOnRead = skipIndexScanOnRead;
			}

			public int Count => _results.Count;

			public EventRecord[] ProduceArray() {
				var array = _results.ToArray();

				if (!_skipIndexScanOnRead) {
					// we already deduplicated on the way in, just sort here
					Array.Sort(array, _byEventNumberDesc);
				}

				return array;
			}

			public List<EventRecord> ProduceList() {
				var list = _results.ToList();

				if (!_skipIndexScanOnRead) {
					list.Sort(_byEventNumberDesc);
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
}
