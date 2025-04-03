// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Projections.Core.Services.Processing.MultiStream;

public class MultiStreamEventFilter : EventFilter {
	private readonly HashSet<string> _streams;

	public MultiStreamEventFilter(HashSet<string> streams, bool allEvents, HashSet<string> events)
		: base(allEvents, false, events) {
		_streams = streams;
	}

	protected override bool DeletedNotificationPasses(string positionStreamId) {
		return false;
	}

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType) {
		return _streams.Contains(positionStreamId);
	}

	public override string GetCategory(string positionStreamId) {
		return null;
	}
}
