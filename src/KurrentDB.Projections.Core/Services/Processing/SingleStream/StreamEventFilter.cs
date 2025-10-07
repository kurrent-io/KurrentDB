// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace KurrentDB.Projections.Core.Services.Processing.SingleStream;

public class StreamEventFilter(string streamId, bool allEvents, HashSet<string> events) : EventFilter(allEvents, false, events) {
	protected override bool DeletedNotificationPasses(string positionStreamId) {
		return positionStreamId == streamId;
	}

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType) {
		return positionStreamId == streamId;
	}

	public override string GetCategory(string positionStreamId) {
		return null;
	}

	public override string ToString() {
		return $"StreamId: {streamId}";
	}
}
