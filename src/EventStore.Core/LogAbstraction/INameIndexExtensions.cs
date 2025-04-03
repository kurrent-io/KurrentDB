// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.LogAbstraction;

public static class INameIndexExtensions {
	// todo: rename to GetOrReserveStream when we generalise to EventTypes too.
	/// Generates a StreamRecord if necessary
	public static bool GetOrReserve<TStreamId>(
		this INameIndex<TStreamId> streamNameIndex,
		IRecordFactory<TStreamId> recordFactory,
		string streamName,
		long logPosition,
		out TStreamId streamId,
		out IPrepareLogRecord<TStreamId> streamRecord) {

		var preExisting = streamNameIndex.GetOrReserve(streamName, out streamId, out var addedId, out var addedName);

		var appendNewStream = recordFactory.ExplicitStreamCreation && !preExisting;
		if (!appendNewStream) {
			streamRecord = null;
			return preExisting;
		}

		streamRecord = recordFactory.CreateStreamRecord(
			streamId: Guid.NewGuid(),
			logPosition: logPosition,
			timeStamp: DateTime.UtcNow,
			streamNumber: addedId,
			streamName: addedName);

		return preExisting;
	}
	
	public static bool GetOrReserveEventType<TStreamId>(
		this INameIndex<TStreamId> eventTypeIndex,
		IRecordFactory<TStreamId> recordFactory,
		string eventType,
		long logPosition,
		out TStreamId eventTypeId,
		out IPrepareLogRecord<TStreamId> eventTypeRecord) {

		var preExisting = eventTypeIndex.GetOrReserve(eventType, out eventTypeId, out var addedNumber, out var addedName);

		var appendNewEventType = recordFactory.ExplicitEventTypeCreation && !preExisting;
		if (!appendNewEventType) {
			eventTypeRecord = null;
			return preExisting;
		}

		eventTypeRecord = recordFactory.CreateEventTypeRecord(
			eventTypeId: Guid.NewGuid(),
			parentEventTypeId: Guid.Empty,
			eventType: addedName,
			eventTypeNumber: addedNumber,
			eventTypeVersion: 0,
			logPosition: logPosition,
			timeStamp: DateTime.UtcNow);

		return preExisting;
	}

	public static TStreamId GetExisting<TStreamId>(
		this INameIndex<TStreamId> nameIndex,
		string name) {

		if (!nameIndex.GetOrReserve(name, out var value, out _, out _))
			throw new Exception($"{name} was expected to already exist but it didn not");

		return value;
	}
}
