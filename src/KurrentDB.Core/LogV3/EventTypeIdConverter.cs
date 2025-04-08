// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.LogV3;

/// Converts between EventTypeIds and their event number in the event types stream.
public static class EventTypeIdConverter {
	static readonly uint _offset = LogV3SystemEventTypes.FirstRealEventTypeNumber;

	public static uint ToEventTypeId(long index) {
		return (uint)index + _offset;
	}

	public static long ToEventNumber(uint eventTypeId) {
		return eventTypeId - _offset;
	}
}
