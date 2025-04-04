// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Services.Processing.Partitioning;

public class ByStreamStatePartitionSelector : StatePartitionSelector {
	public override string GetStatePartition(EventReaderSubscriptionMessage.CommittedEventReceived @event) {
		if (@event.Data.ResolvedLinkTo && @event.Data.PositionMetadata != null) {
			var extra = @event.Data.PositionMetadata.ParseCheckpointExtraJson();
			JToken v;
			if (extra != null && extra.TryGetValue("$o", out v)) {
				//TODO: handle exceptions properly
				var originalStream = (string)((JValue)v).Value;
				return originalStream;
			}
		}

		var eventStreamId = @event.Data.EventStreamId;
		return SystemStreams.IsMetastream(eventStreamId)
			? eventStreamId.Substring("$$".Length)
			: eventStreamId;
	}

	public override bool EventReaderBasePartitionDeletedIsSupported() {
		return true;
	}
}
