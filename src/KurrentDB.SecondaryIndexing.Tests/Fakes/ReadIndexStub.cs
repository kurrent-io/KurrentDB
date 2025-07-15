// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using NSubstitute;

namespace KurrentDB.SecondaryIndexing.Tests.Fakes;

public class ReadIndexStub {
	public IReadIndex<string> ReadIndex { get; }
	private readonly ITransactionFileReader _transactionalFileReader;

	public static IReadIndex<string> Build() => new ReadIndexStub().ReadIndex;

	public ReadIndexStub() {
		_transactionalFileReader = Substitute.For<ITransactionFileReader>();

		var lease = new TFReaderLease(
			new ObjectPool<ITransactionFileReader>("dummy", 1, 1, () => _transactionalFileReader));

		var backend = Substitute.For<IIndexBackend<string>>();

		var indexReader = Substitute.For<IIndexReader<string>>();
		indexReader.Backend.Returns(backend);
		indexReader.BorrowReader().Returns(lease);

		ReadIndex = Substitute.For<IReadIndex<string>>();
		ReadIndex.IndexReader.Returns(indexReader);
	}

	public void IndexEvents(ResolvedEvent[] events) {
		_transactionalFileReader.TryReadAt(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.ReturnsForAnyArgs(x => {
				var logPosition = x.ArgAt<long>(0);

				if (!events.Any(e => e.Event.LogPosition == logPosition))
					return new RecordReadResult();

				var evnt = events.Single(e => e.Event.LogPosition == logPosition).Event;

				var prepare = new PrepareLogRecord(
					logPosition,
					evnt.CorrelationId,
					evnt.EventId,
					evnt.TransactionPosition,
					evnt.TransactionOffset,
					evnt.EventStreamId,
					null,
					evnt.ExpectedVersion,
					evnt.TimeStamp,
					evnt.Flags,
					evnt.EventType,
					null,
					evnt.Data,
					evnt.Metadata,
					evnt.Properties
				);

				return new RecordReadResult(true, -1, prepare, evnt.Data.Length);
			});

		ReadIndex.LastIndexedPosition.Returns(events.Last().Event.LogPosition);
	}
}
