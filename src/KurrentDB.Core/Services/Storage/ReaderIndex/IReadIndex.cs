// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public interface IReadIndex {
	long LastIndexedPosition { get; }

	ReadIndexStats GetStatistics();

	/// <summary>
	/// Returns event records in the sequence they were committed into TF.
	/// Positions is specified as pre-positions (pointer at the beginning of the record).
	/// </summary>
	ValueTask<IndexReadAllResult> ReadAllEventsForward(TFPos pos, int maxCount, CancellationToken token);

	/// <summary>
	/// Returns event records in the reverse sequence they were committed into TF.
	/// Positions is specified as post-positions (pointer after the end of record).
	/// </summary>
	ValueTask<IndexReadAllResult> ReadAllEventsBackward(TFPos pos, int maxCount, CancellationToken token);

	/// <summary>
	/// Returns event records whose eventType matches the given EventFilter in the sequence they were committed into TF.
	/// Positions is specified as pre-positions (pointer at the beginning of the record).
	/// </summary>
	ValueTask<IndexReadAllResult> ReadAllEventsForwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter, CancellationToken token);

	/// <summary>
	/// Returns event records whose eventType matches the given EventFilter in the sequence they were committed into TF.
	/// Positions is specified as pre-positions (pointer at the beginning of the record).
	/// </summary>
	ValueTask<IndexReadAllResult> ReadAllEventsBackwardFiltered(TFPos pos, int maxCount, int maxSearchWindow,
		IEventFilter eventFilter, CancellationToken token);

	void Close();
	void Dispose();
}

public interface IReadIndex<TStreamId> : IReadIndex {
	IIndexWriter<TStreamId> IndexWriter { get; }
	IIndexReader<TStreamId> IndexReader { get; }

	// ReadEvent() / ReadStreamEvents*() :
	// - deleted events are filtered out
	// - duplicates are removed, keeping only the earliest event in the log
	// - streamId drives the read, streamName is only for populating on the result.
	//   this was less messy than safely adding the streamName to the EventRecord at some point after construction.
	ValueTask<IndexReadEventResult> ReadEvent(string streamName, TStreamId streamId, long eventNumber, CancellationToken token);
	ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token);
	ValueTask<IndexReadStreamResult> ReadStreamEventsForward(string streamName, TStreamId streamId, long fromEventNumber, int maxCount, CancellationToken token);

	// ReadEventInfo_KeepDuplicates() :
	// - deleted events are not filtered out
	// - duplicates are kept, in ascending order of log position
	// - next event number is always -1
	ValueTask<IndexReadEventInfoResult> ReadEventInfo_KeepDuplicates(TStreamId streamId, long eventNumber, CancellationToken token);

	// ReadEventInfo*Collisions() :
	// - deleted events are not filtered out
	// - duplicates are removed, keeping only the earliest event in the log
	// - only events that are before "beforePosition" in the transaction log are returned
	ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoForward_NoCollisions(ulong stream, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_KnownCollisions(TStreamId streamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);
	ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long fromEventNumber, int maxCount, long beforePosition, CancellationToken token);

	ValueTask<bool> IsStreamDeleted(TStreamId streamId, CancellationToken token);
	ValueTask<long> GetStreamLastEventNumber(TStreamId streamId, CancellationToken token);
	ValueTask<long> GetStreamLastEventNumber_KnownCollisions(TStreamId streamId, long beforePosition, CancellationToken token);
	ValueTask<long> GetStreamLastEventNumber_NoCollisions(ulong stream, Func<ulong, TStreamId> getStreamId, long beforePosition, CancellationToken token);
	ValueTask<StreamMetadata> GetStreamMetadata(TStreamId streamId, CancellationToken token);
	ValueTask<StorageMessage.EffectiveAcl> GetEffectiveAcl(TStreamId streamId, CancellationToken token);
	ValueTask<TStreamId> GetEventStreamIdByTransactionId(long transactionId, CancellationToken token);

	TStreamId GetStreamId(string streamName);
	ValueTask<string> GetStreamName(TStreamId streamId, CancellationToken token);
}
