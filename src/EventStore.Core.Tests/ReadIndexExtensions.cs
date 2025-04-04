// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Tests;

// Extensions to perform streamlookups inline
// mainly to facilitate conversion of existing LogV2 tests.
public static class IReadIndexExtensions {
	public static bool IsStreamDeleted<TStreamId>(this IReadIndex<TStreamId> self, string streamName) {
		var streamId = self.GetStreamId(streamName);
		return self.IsStreamDeleted(streamId);
	}

	public static long GetStreamLastEventNumber<TStreamId>(this IReadIndex<TStreamId> self, string streamName) {
		var streamId = self.GetStreamId(streamName);
		return self.GetStreamLastEventNumber(streamId);
	}

	public static IndexReadEventResult ReadEvent<TStreamId>(this IReadIndex<TStreamId> self, string streamName, long eventNumber) {
		var streamId = self.GetStreamId(streamName);
		return self.ReadEvent(streamName, streamId, eventNumber);
	}

	public static IndexReadStreamResult ReadStreamEventsForward<TStreamId>(this IReadIndex<TStreamId> self, string streamName, long fromEventNumber, int maxCount) {
		var streamId = self.GetStreamId(streamName);
		return self.ReadStreamEventsForward(streamName, streamId, fromEventNumber, maxCount);
	}

	public static IndexReadStreamResult ReadStreamEventsBackward<TStreamId>(this IReadIndex<TStreamId> self, string streamName, long fromEventNumber, int maxCount) {
		var streamId = self.GetStreamId(streamName);
		return self.ReadStreamEventsBackward(streamName, streamId, fromEventNumber, maxCount);
	}

	public static StreamMetadata GetStreamMetadata<TStreamId>(this IReadIndex<TStreamId> self, string streamName) {
		var streamId = self.GetStreamId(streamName);
		return self.GetStreamMetadata(streamId);
	}

	public static List<CommitEventRecord> EventRecords(this IndexReadAllResult result) {
		return result.Records
			.Where(r => r.Event.EventStreamId != SystemStreams.StreamsCreatedStream
						&& r.Event.EventStreamId != SystemStreams.EventTypesStream).ToList();
	}
}
