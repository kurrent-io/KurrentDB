// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace KurrentDB.Projections.Core.Services.Processing.MultiStream;

public class MultiStreamEventFilter(HashSet<string> streams, bool allEvents, HashSet<string> events)
	: EventFilter(allEvents, false, events) {
	protected override bool DeletedNotificationPasses(string positionStreamId) => false;

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType)
		=> streams.Contains(positionStreamId);

	public override string GetCategory(string positionStreamId) => null;
}
