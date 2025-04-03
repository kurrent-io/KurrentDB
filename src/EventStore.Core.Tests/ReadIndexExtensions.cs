// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Tests;

// Extensions to perform streamlookups inline
// mainly to facilitate conversion of existing LogV2 tests.
public static class IReadIndexExtensions {
	public static ValueTask<bool> IsStreamDeleted<TStreamId>(this IReadIndex<TStreamId> self, string streamName, CancellationToken token) {
		var streamId = self.GetStreamId(streamName);
		return self.IsStreamDeleted(streamId, token);
	}

	public static ValueTask<long> GetStreamLastEventNumber<TStreamId>(this IReadIndex<TStreamId> self, string streamName, CancellationToken token) {
		var streamId = self.GetStreamId(streamName);
		return self.GetStreamLastEventNumber(streamId, token);
	}

	public static ValueTask<IndexReadEventResult> ReadEvent<TStreamId>(this IReadIndex<TStreamId> self, string streamName, long eventNumber, CancellationToken token) {
		var streamId = self.GetStreamId(streamName);
		return self.ReadEvent(streamName, streamId, eventNumber, token);
	}

	public static ValueTask<IndexReadStreamResult> ReadStreamEventsForward<TStreamId>(this IReadIndex<TStreamId> self, string streamName, long fromEventNumber, int maxCount, CancellationToken token) {
		var streamId = self.GetStreamId(streamName);
		return self.ReadStreamEventsForward(streamName, streamId, fromEventNumber, maxCount, token);
	}

	public static ValueTask<IndexReadStreamResult> ReadStreamEventsBackward<TStreamId>(this IReadIndex<TStreamId> self, string streamName, long fromEventNumber, int maxCount, CancellationToken token) {
		var streamId = self.GetStreamId(streamName);
		return self.ReadStreamEventsBackward(streamName, streamId, fromEventNumber, maxCount, token);
	}

	public static ValueTask<StreamMetadata> GetStreamMetadata<TStreamId>(this IReadIndex<TStreamId> self, string streamName, CancellationToken token) {
		var streamId = self.GetStreamId(streamName);
		return self.GetStreamMetadata(streamId, token);
	}

	public static List<CommitEventRecord> EventRecords(this IndexReadAllResult result) {
		return result.Records
			.Where(r => r.Event.EventStreamId != SystemStreams.StreamsCreatedStream
			            && r.Event.EventStreamId != SystemStreams.EventTypesStream).ToList();
	}
}
