// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;

namespace EventStore.Projections.Core.Services.Processing;

public abstract class EventFilter {
	private readonly bool _allEvents;
	private readonly bool _includeDeletedStreamEvents;
	private readonly HashSet<string> _events;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="allEvents"></param>
	/// <param name="includeDeletedStreamEvents">indicates whether non-link stream tombstone or 
	/// metastream stream deleted events should be included. Links resolved into metastream 
	/// stream deleted events are not restricted</param>
	/// <param name="events"></param>
	protected EventFilter(bool allEvents, bool includeDeletedStreamEvents, HashSet<string> events) {
		_allEvents = allEvents;
		_includeDeletedStreamEvents = includeDeletedStreamEvents;
		_events = events;
	}

	public bool Passes(
		bool resolvedFromLinkTo, string eventStreamId, string eventName, bool isStreamDeletedEvent = false) {
		return (PassesSource(resolvedFromLinkTo, eventStreamId, eventName))
		       && ((_allEvents || _events != null && _events.Contains(eventName))
		           && (!isStreamDeletedEvent || _includeDeletedStreamEvents));
	}

	public bool PassesValidation(bool isJson, ReadOnlyMemory<byte> data) {
		return !isJson || data.IsValidUtf8Json();
	}

	protected abstract bool DeletedNotificationPasses(string positionStreamId);
	public abstract bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType);
	public abstract string GetCategory(string positionStreamId);

	public bool PassesDeleteNotification(string positionStreamId) {
		return !_includeDeletedStreamEvents && DeletedNotificationPasses(positionStreamId);
	}
}
