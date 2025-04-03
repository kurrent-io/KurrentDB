// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Projections.Core.Utils;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Standard;

public static class StreamDeletedHelper {
	public static bool IsStreamDeletedEventOrLinkToStreamDeletedEvent(ResolvedEvent resolvedEvent,
		ReadEventResult resolveResult, out string deletedPartitionStreamId) {
		bool isDeletedStreamEvent;
		// If the event didn't resolve, we can't rely on it as a deleted event
		if (resolveResult != ReadEventResult.Success) {
			deletedPartitionStreamId = null;
			return false;
		}
		
		if (resolvedEvent.IsLinkToDeletedStreamTombstone) {
			isDeletedStreamEvent = true;
			deletedPartitionStreamId = resolvedEvent.EventStreamId;
		} else {
			isDeletedStreamEvent = StreamDeletedHelper.IsStreamDeletedEvent(
				resolvedEvent.EventStreamId, resolvedEvent.EventType, resolvedEvent.Data,
				out deletedPartitionStreamId);
		}

		return isDeletedStreamEvent;
	}

	public static bool IsStreamDeletedEvent(
		string streamOrMetaStreamId, string eventType, string eventData, out string deletedPartitionStreamId) {
		if (string.IsNullOrEmpty(streamOrMetaStreamId)) {
			deletedPartitionStreamId = null;
			return false;
		}

		bool isMetaStream;
		if (SystemStreams.IsMetastream(streamOrMetaStreamId)) {
			isMetaStream = true;
			deletedPartitionStreamId = streamOrMetaStreamId.Substring("$$".Length);
		} else {
			isMetaStream = false;
			deletedPartitionStreamId = streamOrMetaStreamId;
		}

		var isStreamDeletedEvent = false;
		if (isMetaStream) {
			if (eventType == SystemEventTypes.StreamMetadata) {
				var metadata = StreamMetadata.FromJson(eventData);
				//NOTE: we do not ignore JSON deserialization exceptions here assuming that metadata stream events must be deserializable

				if (metadata.TruncateBefore == EventNumber.DeletedStream)
					isStreamDeletedEvent = true;
			}
		} else {
			if (eventType == SystemEventTypes.StreamDeleted)
				isStreamDeletedEvent = true;
		}

		return isStreamDeletedEvent;
	}

	public static bool IsStreamDeletedEvent(
		string streamOrMetaStreamId, string eventType, ReadOnlyMemory<byte> eventData, out string deletedPartitionStreamId) {
		bool isMetaStream;
		if (SystemStreams.IsMetastream(streamOrMetaStreamId)) {
			isMetaStream = true;
			deletedPartitionStreamId = streamOrMetaStreamId.Substring("$$".Length);
		} else {
			isMetaStream = false;
			deletedPartitionStreamId = streamOrMetaStreamId;
		}

		var isStreamDeletedEvent = false;
		if (isMetaStream) {
			if (eventType == SystemEventTypes.StreamMetadata) {
				var metadata = StreamMetadata.FromJson(eventData.FromUtf8());
				//NOTE: we do not ignore JSON deserialization exceptions here assuming that metadata stream events must be deserializable

				if (metadata.TruncateBefore == EventNumber.DeletedStream)
					isStreamDeletedEvent = true;
			}
		} else {
			if (eventType == SystemEventTypes.StreamDeleted)
				isStreamDeletedEvent = true;
		}

		return isStreamDeletedEvent;
	}
}
