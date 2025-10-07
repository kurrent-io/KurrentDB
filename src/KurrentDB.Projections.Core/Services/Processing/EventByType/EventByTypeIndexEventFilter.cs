// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Services;

namespace KurrentDB.Projections.Core.Services.Processing.EventByType;

public class EventByTypeIndexEventFilter : EventFilter {
	private readonly HashSet<string> _streams;

	public EventByTypeIndexEventFilter(HashSet<string> events) : base(false, false, events) {
		_streams = new(events.Select(eventType => $"$et-{eventType}"));
	}

	protected override bool DeletedNotificationPasses(string positionStreamId) => true;

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType)
		=> _streams.Contains(positionStreamId) || !resolvedFromLinkTo && !SystemStreams.IsSystemStream(positionStreamId);

	public override string GetCategory(string positionStreamId) => null;
}
