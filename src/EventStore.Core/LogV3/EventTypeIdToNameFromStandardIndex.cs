// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.Data;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.LogV3;

public class EventTypeIdToNameFromStandardIndex : INameLookup<uint> {
	private readonly IIndexReader<uint> _indexReader;

	public EventTypeIdToNameFromStandardIndex(IIndexReader<uint> indexReader) {
		_indexReader = indexReader;
	}

	public async ValueTask<string> LookupName(uint eventTypeId, CancellationToken token) {
		var record = await _indexReader.ReadPrepare(
			streamId: LogV3SystemStreams.EventTypesStreamNumber,
			eventNumber: EventTypeIdConverter.ToEventNumber(eventTypeId),
			token);

		return record switch {
			null => null,
			LogV3EventTypeRecord eventTypeRecord => eventTypeRecord.EventTypeName,
			_ => throw new Exception($"Unexpected log record type: {record}."),
		};
	}

	public async ValueTask<Optional<uint>> TryGetLastValue(CancellationToken token) {
		var lastEventNumber = await _indexReader.GetStreamLastEventNumber(LogV3SystemStreams.EventTypesStreamNumber, token);
		return lastEventNumber is > ExpectedVersion.NoStream and not EventNumber.DeletedStream
			? EventTypeIdConverter.ToEventTypeId(lastEventNumber)
			: Optional.None<uint>();
	}
}
