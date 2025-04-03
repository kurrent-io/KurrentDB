// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services;
using EventTypeId = System.UInt32;

namespace EventStore.Core.LogV3;

public class LogV3SystemEventTypes {

	public const EventTypeId FirstRealEventTypeNumber = 1024;
	public const EventTypeId EventTypeInterval = 1;

	public const EventTypeId EmptyEventTypeNumber = 0;
	public const EventTypeId EventTypeDefinedNumber = 1;
	public const EventTypeId StreamCreatedNumber = 2;
	public const EventTypeId StreamMetadataNumber = 3;
	public const EventTypeId StreamDeletedNumber = 4;
	public const EventTypeId EpochInformationNumber = 5;
	public const EventTypeId ScavengePointNumber = 6;

	public static bool TryGetSystemEventTypeId(string type, out EventTypeId eventTypeId) {
		switch (type) {
			case SystemEventTypes.EmptyEventType:
				eventTypeId = EmptyEventTypeNumber;
				return true;
			case SystemEventTypes.EpochInformation:
				eventTypeId = EpochInformationNumber;
				return true;
			case SystemEventTypes.EventTypeDefined:
				eventTypeId = EventTypeDefinedNumber;
				return true;
			case SystemEventTypes.ScavengePoint:
				eventTypeId = ScavengePointNumber;
				return true;
			case SystemEventTypes.StreamCreated:
				eventTypeId = StreamCreatedNumber;
				return true;
			case SystemEventTypes.StreamMetadata:
				eventTypeId = StreamMetadataNumber;
				return true;
			case SystemEventTypes.StreamDeleted:
				eventTypeId = StreamDeletedNumber;
				return true;
			default:
				eventTypeId = EmptyEventTypeNumber;
				return false;
		}
	}

	public static bool TryGetVirtualEventType(EventTypeId eventTypeId, out string name) {
		if (!IsVirtualEventType(eventTypeId)) {
			name = null;
			return false;
		}

		name = eventTypeId switch {
			EmptyEventTypeNumber => SystemEventTypes.EmptyEventType,
			EpochInformationNumber => SystemEventTypes.EpochInformation,
			EventTypeDefinedNumber => SystemEventTypes.EventTypeDefined,
			ScavengePointNumber => SystemEventTypes.ScavengePoint,
			StreamCreatedNumber => SystemEventTypes.StreamCreated,
			StreamMetadataNumber => SystemEventTypes.StreamMetadata,
			StreamDeletedNumber => SystemEventTypes.StreamDeleted,
			_ => null,
		};

		return name != null;
	}

	private static bool IsVirtualEventType(EventTypeId eventTypeId) => eventTypeId < FirstRealEventTypeNumber;
}
