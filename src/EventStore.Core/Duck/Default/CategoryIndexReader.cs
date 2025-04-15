// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Duck.Default;

class CategoryIndexReader<TStreamId>(CategoryIndex categoryIndex, IReadIndex<TStreamId> index) : DuckIndexReader<TStreamId>(index) {
	protected override long GetId(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		if (dashIndex == -1) {
			return ExpectedVersion.Invalid;
		}

		var category = streamName[(dashIndex + 1)..];
		return categoryIndex.Categories.TryGetValue(category, out var id) ? id : ExpectedVersion.NoStream;
	}

	protected override long GetLastNumber(long id) => categoryIndex.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber)
		=> categoryIndex.GetRecords(id, fromEventNumber, toEventNumber);

	public override ValueTask<long> GetLastIndexedPosition() => ValueTask.FromResult(categoryIndex.LastPosition);

	public override bool OwnStream(string streamId) => streamId.StartsWith("$cat-");
}
