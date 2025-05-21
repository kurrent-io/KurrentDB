// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Tests.Services.Storage.ReadIndex;

class FakeIndexReader<TStreamId> : IIndexReader<TStreamId> {
	public long CachedStreamInfo { get; }
	public long NotCachedStreamInfo { get; }
	public long HashCollisions { get; }

	public ValueTask<IndexReadEventResult> ReadEvent(string streamName, TStreamId streamId, long eventNumber,
		CancellationToken token)
		=> ValueTask.FromException<IndexReadEventResult>(new NotImplementedException());

	public ValueTask<IndexReadStreamResult>
		ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount,
			CancellationToken token)
		=> ValueTask.FromException<IndexReadStreamResult>(new NotImplementedException());

	public ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(string streamName, TStreamId streamId,
		long fromEventNumber,
		int maxCount, CancellationToken token)
		=> ValueTask.FromException<IndexReadStreamResult>(new NotImplementedException());

	public ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(TStreamId streamId, CancellationToken token)
		=> ValueTask.FromException<StorageMessage.EffectiveAcl>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber,
		CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(TStreamId streamId,
		long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long fromEventNumber,
		int maxCount, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepare(TStreamId streamId, long eventNumber,
		CancellationToken token)
		=> ValueTask.FromException<IPrepareLogRecord<TStreamId>>(new NotImplementedException());

	public ValueTask<TStreamId> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token)
		=> ValueTask.FromException<TStreamId>(new NotImplementedException());

	public ValueTask<StreamMetadata> GetStreamMetadata(TStreamId streamId, CancellationToken token)
		=> ValueTask.FromException<StreamMetadata>(new NotImplementedException());

	public ValueTask<long> GetStreamLastEventNumber(TStreamId streamId, CancellationToken token)
		=> ValueTask.FromException<long>(new NotImplementedException());

	public ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<long>(new NotImplementedException());

	public ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<long>(new NotImplementedException());

	public TFReaderLease BorrowReader() {
		throw new NotImplementedException();
	}
}
