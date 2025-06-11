// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Duck.Default;

class EventTypeIndexReader(EventTypeIndex eventTypeIndex, IReadIndex<string> index) : DuckIndexReader(index) {
	const string Prefix = "$idx-et-";

	protected override long GetId(string streamName) {
		if (!streamName.StartsWith(Prefix)) {
			return EventNumber.Invalid;
		}

		var eventType = streamName[8..];
		return eventTypeIndex.EventTypes.TryGetValue(eventType, out var id) ? id : ExpectedVersion.NoStream;
	}

	protected override long GetLastNumber(long id) => eventTypeIndex.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber)
		=> eventTypeIndex.GetRecords(id, fromEventNumber, toEventNumber);

	public override ValueTask<long> GetLastIndexedPosition() => ValueTask.FromResult(eventTypeIndex.LastPosition);

	public override bool OwnStream(string streamId) => streamId.StartsWith(Prefix);
}
