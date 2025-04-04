// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Projections.Core.Services.Processing.SingleStream;

public class StreamEventFilter : EventFilter {
	private readonly string _streamId;

	public StreamEventFilter(string streamId, bool allEvents, HashSet<string> events)
		: base(allEvents, false, events) {
		_streamId = streamId;
	}

	protected override bool DeletedNotificationPasses(string positionStreamId) {
		return positionStreamId == _streamId;
	}

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType) {
		return positionStreamId == _streamId;
	}

	public override string GetCategory(string positionStreamId) {
		return null;
	}

	public override string ToString() {
		return string.Format("StreamId: {0}", _streamId);
	}
}
