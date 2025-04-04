using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Tests.Services.Storage;

public class FakeIndexWriter<TStreamId> : IIndexWriter<TStreamId> {
	public long CachedTransInfo => 0;
	public long NotCachedTransInfo => 0;
	public void Reset() { }

	private TStreamId GetFakeStreamId() {
		if (typeof(TStreamId) == typeof(long)) {
			return (TStreamId)(object)0L;
		}

		if (typeof(TStreamId) == typeof(string)) {
			return (TStreamId)(object)string.Empty;
		}

		throw new NotSupportedException();
	}

	public CommitCheckResult<TStreamId> CheckCommitStartingAt(long transactionPosition, long commitPosition) {
		return new CommitCheckResult<TStreamId>(CommitDecision.Ok, GetFakeStreamId(), -1, -1, -1, false);
	}

	public CommitCheckResult<TStreamId> CheckCommit(TStreamId streamId, long expectedVersion, IEnumerable<Guid> eventIds, bool streamMightExist) {
		return new CommitCheckResult<TStreamId>(CommitDecision.Ok, streamId, expectedVersion, -1, -1, false);
	}

	public void PreCommit(CommitLogRecord commit) { }

	public void PreCommit(ReadOnlySpan<IPrepareLogRecord<TStreamId>> committedPrepares) { }

	public void UpdateTransactionInfo(long transactionId, long logPosition, TransactionInfo<TStreamId> transactionInfo) { }

	public TransactionInfo<TStreamId> GetTransactionInfo(long writerCheckpoint, long transactionId) => new();

	public void PurgeNotProcessedCommitsTill(long checkpoint) { }

	public void PurgeNotProcessedTransactions(long checkpoint) { }

	public bool IsSoftDeleted(TStreamId streamId) => false;

	public long GetStreamLastEventNumber(TStreamId streamId) => -1;

	public StreamMetadata GetStreamMetadata(TStreamId streamId) => StreamMetadata.Empty;

	public RawMetaInfo GetStreamRawMeta(TStreamId streamId) => new();

	public TStreamId GetStreamId(string streamName) => GetFakeStreamId();

	public string GetStreamName(TStreamId streamId) => string.Empty;
}
