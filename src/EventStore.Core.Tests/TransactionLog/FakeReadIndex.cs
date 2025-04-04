using System;
using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Tests.TransactionLog {
	public class FakeReadIndex<TLogFormat, TStreamId> : IReadIndex<TStreamId> {
		private readonly IMetastreamLookup<TStreamId> _metastreams;

		public long LastIndexedPosition {
			get { return 0; }
		}

		public IIndexWriter<TStreamId> IndexWriter {
			get { throw new NotImplementedException(); }
		}

		private readonly Func<TStreamId, bool> _isStreamDeleted;

		public FakeReadIndex(
			Func<TStreamId, bool> isStreamDeleted,
			IMetastreamLookup<TStreamId> metastreams) {

			Ensure.NotNull(isStreamDeleted, "isStreamDeleted");
			_isStreamDeleted = isStreamDeleted;
			_metastreams = metastreams;
		}

		public void Init(long buildToPosition) {
			throw new NotImplementedException();
		}

		public void Commit(CommitLogRecord record) {
			throw new NotImplementedException();
		}

		public void Commit(IList<IPrepareLogRecord<TStreamId>> commitedPrepares) {
			throw new NotImplementedException();
		}

		public ReadIndexStats GetStatistics() {
			throw new NotImplementedException();
		}

		public IndexReadEventResult ReadEvent(string streamName, TStreamId streamId, long eventNumber) {
			throw new NotImplementedException();
		}

		public IndexReadStreamResult ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount) {
			throw new NotImplementedException();
		}

		public IndexReadStreamResult ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount) {
			throw new NotImplementedException();
		}

		public IndexReadEventInfoResult ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber) {
			throw new NotImplementedException();
		}

		public IndexReadEventInfoResult ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
			long beforePosition) {
			throw new NotImplementedException();
		}

		public IndexReadEventInfoResult ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition) {
			throw new NotImplementedException();
		}

		public IndexReadEventInfoResult ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
			long beforePosition) {
			throw new NotImplementedException();
		}

		public IndexReadEventInfoResult ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long fromEventNumber,
			int maxCount, long beforePosition) {
			throw new NotImplementedException();
		}

		public IndexReadAllResult ReadAllEventsForward(TFPos pos, int maxCount) {
			throw new NotImplementedException();
		}

		public IndexReadAllResult ReadAllEventsBackward(TFPos pos, int maxCount) {
			throw new NotImplementedException();
		}

		public IndexReadAllResult ReadAllEventsForwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
			IEventFilter eventFilter) {
			throw new NotImplementedException();
		}

		public IndexReadAllResult ReadAllEventsBackwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
			IEventFilter eventFilter) {
			throw new NotImplementedException();
		}

		public bool IsStreamDeleted(TStreamId streamId) {
			return _isStreamDeleted(streamId);
		}

		public long GetStreamLastEventNumber(TStreamId streamId) {
			if (_metastreams.IsMetaStream(streamId))
				return GetStreamLastEventNumber(_metastreams.OriginalStreamOf(streamId));
			return _isStreamDeleted(streamId) ? EventNumber.DeletedStream : 1000000;
		}

		public long GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition) {
			throw new NotImplementedException();
		}

		public long GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition) {
			throw new NotImplementedException();
		}

		public StorageMessage.EffectiveAcl GetEffectiveAcl(TStreamId streamId) {
			throw new NotImplementedException();
		}

		public TStreamId GetEventStreamIdByTransactionId(long transactionId) {
			throw new NotImplementedException();
		}

		public StreamAccess CheckStreamAccess(TStreamId streamId, StreamAccessType streamAccessType, ClaimsPrincipal user) {
			throw new NotImplementedException();
		}

		public StreamMetadata GetStreamMetadata(TStreamId streamId) {
			throw new NotImplementedException();
		}

		public TStreamId GetStreamId(string streamName) {
			throw new NotImplementedException();
		}

		public string GetStreamName(TStreamId streamId) {
			throw new NotImplementedException();
		}

		public void Close() {
			throw new NotImplementedException();
		}

		public void Dispose() {
			throw new NotImplementedException();
		}
	}
}
