// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Services;

namespace KurrentDB.Projections.Core.Services.Processing.TransactionFile;

public class TransactionFileEventFilter(bool allEvents, bool includeDeletedStreamEvents, HashSet<string> events, bool includeLinks = false)
	: EventFilter(allEvents, includeDeletedStreamEvents, events) {
	protected override bool DeletedNotificationPasses(string positionStreamId) => true;

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType) {
		if (!includeLinks && eventType == SystemEventTypes.LinkTo)
			return false;
		return (includeLinks || !resolvedFromLinkTo)
		       && (!SystemStreams.IsSystemStream(positionStreamId)
		           || SystemStreams.IsMetastream(positionStreamId)
		           && !SystemStreams.IsSystemStream(SystemStreams.OriginalStreamOf(positionStreamId)));
	}

	public override string GetCategory(string positionStreamId) => null;
}
