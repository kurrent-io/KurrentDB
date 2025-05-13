// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Tests.TransactionLog;

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

	public void Commit(IList<IPrepareLogRecord<TStreamId>> committedPrepares) {
		throw new NotImplementedException();
	}

	public ReadIndexStats GetStatistics() {
		throw new NotImplementedException();
	}

	public ValueTask<IndexReadEventResult> ReadEvent(string streamName, TStreamId streamId, long eventNumber,
		CancellationToken token)
		=> ValueTask.FromException<IndexReadEventResult>(new NotImplementedException());

	public ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token)
		=> ValueTask.FromException<IndexReadStreamResult>(new NotImplementedException());

	public ValueTask<IndexReadStreamResult> ReadStreamEventsForward(string streamName, TStreamId streamId,
		long fromEventNumber, int maxCount, CancellationToken token)
		=> ValueTask.FromException<IndexReadStreamResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount,
		long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long fromEventNumber,
		int maxCount, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<IndexReadEventInfoResult>(new NotImplementedException());

	public ValueTask<IndexReadAllResult> ReadAllEventsForward(TFPos pos, int maxCount, CancellationToken token)
		=> ValueTask.FromException<IndexReadAllResult>(new NotImplementedException());

	public ValueTask<IndexReadAllResult> ReadAllEventsBackward(TFPos pos, int maxCount, CancellationToken token)
		=> ValueTask.FromException<IndexReadAllResult>(new NotImplementedException());

	public ValueTask<IndexReadAllResult> ReadAllEventsForwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter, CancellationToken token)
		=> ValueTask.FromException<IndexReadAllResult>(new NotImplementedException());

	public ValueTask<IndexReadAllResult> ReadAllEventsBackwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter, CancellationToken token)
		=> ValueTask.FromException<IndexReadAllResult>(new NotImplementedException());

	public ValueTask<bool> IsStreamDeleted(TStreamId streamId, CancellationToken token) {
		return new(_isStreamDeleted(streamId));
	}

	public ValueTask<long> GetStreamLastEventNumber(TStreamId streamId, CancellationToken token) {
		if (_metastreams.IsMetaStream(streamId))
			return GetStreamLastEventNumber(_metastreams.OriginalStreamOf(streamId), token);

		return new(_isStreamDeleted(streamId) ? EventNumber.DeletedStream : 1000000);
	}

	public ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<long>(new NotImplementedException());

	public ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition, CancellationToken token)
		=> ValueTask.FromException<long>(new NotImplementedException());

	public ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(TStreamId streamId, CancellationToken token)
		=> ValueTask.FromException<StorageMessage.EffectiveAcl>(new NotImplementedException());

	public ValueTask<TStreamId> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token)
		=> ValueTask.FromException<TStreamId>(new NotImplementedException());

	public StreamAccess CheckStreamAccess(TStreamId streamId, StreamAccessType streamAccessType, ClaimsPrincipal user) {
		throw new NotImplementedException();
	}

	public ValueTask<StreamMetadata> GetStreamMetadata(TStreamId streamId, CancellationToken token)
		=> ValueTask.FromException<StreamMetadata>(new NotImplementedException());

	public TStreamId GetStreamId(string streamName) {
		throw new NotImplementedException();
	}

	public ValueTask<string> GetStreamName(TStreamId streamId, CancellationToken token)
		=> ValueTask.FromException<string>(new NotImplementedException());

	public void Close() {
		throw new NotImplementedException();
	}

	public void Dispose() {
		throw new NotImplementedException();
	}
}
