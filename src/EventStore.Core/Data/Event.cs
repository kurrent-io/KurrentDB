// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Data;

public class Event {
	public readonly Guid EventId;
	public readonly string EventType;
	public readonly bool IsJson;
	public readonly byte[] Data;
	public readonly byte[] Metadata;

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata)
		: this(
			eventId, eventType, isJson, Helper.UTF8NoBom.GetBytes(data),
			metadata != null ? Helper.UTF8NoBom.GetBytes(metadata) : null) {
	}

	public static int SizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		(data?.Length ?? 0) + (metadata?.Length ?? 0) + (eventType.Length * 2);

	private static bool ExceedsMaximumSizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		SizeOnDisk(eventType, data, metadata) > TFConsts.EffectiveMaxLogRecordSize;

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata) {
		if (eventId == Guid.Empty)
			throw new ArgumentException("Empty eventId provided.", nameof(eventId));
		if (string.IsNullOrEmpty(eventType))
			throw new ArgumentException("Empty eventType provided.", nameof(eventType));
		if (ExceedsMaximumSizeOnDisk(eventType, data, metadata))
			throw new ArgumentException("Record is too big.", nameof(data));

		EventId = eventId;
		EventType = eventType;
		IsJson = isJson;
		Data = data ?? Array.Empty<byte>();
		Metadata = metadata ?? Array.Empty<byte>();
	}
}
