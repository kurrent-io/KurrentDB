// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV3;

public class EventTypeIndexSystemTypesDecorator : INameIndex<uint> {
	private readonly INameIndex<uint> _wrapped;

	public EventTypeIndexSystemTypesDecorator(INameIndex<uint> wrapped) {
		_wrapped = wrapped;
	}

	public void CancelReservations() {
		_wrapped.CancelReservations();
	}

	public bool GetOrReserve(string eventType, out uint eventTypeId, out uint createdId, out string createdName) {
		Ensure.NotNull(eventType, "eventType");

		if (LogV3SystemEventTypes.TryGetSystemEventTypeId(eventType, out eventTypeId)) {
			createdId = default;
			createdName = default;
			return true;
		}

		return _wrapped.GetOrReserve(eventType, out eventTypeId, out createdId, out createdName);
	}
}
