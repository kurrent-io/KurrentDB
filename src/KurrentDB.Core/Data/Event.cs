// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.Core.Data;

public class Event {
	public readonly Guid EventId;
	public readonly string EventType;
	public readonly bool IsJson;
	public readonly byte[] Data;
	public readonly byte[] Metadata;
	public readonly byte[] Properties;

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata, byte[] properties)
		: this(
			eventId, eventType, isJson, Helper.UTF8NoBom.GetBytes(data),
			metadata != null ? Helper.UTF8NoBom.GetBytes(metadata) : null,
			properties) {
	}

	public static int SizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		(data?.Length ?? 0) + (metadata?.Length ?? 0) + (eventType.Length * 2);

	private static bool ExceedsMaximumSizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		SizeOnDisk(eventType, data, metadata) > TFConsts.EffectiveMaxLogRecordSize;

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata, byte[] properties) {
		if (eventId == Guid.Empty)
			throw new ArgumentException("Empty eventId provided.", nameof(eventId));
		if (string.IsNullOrEmpty(eventType))
			throw new ArgumentException("Empty eventType provided.", nameof(eventType));
		if (ExceedsMaximumSizeOnDisk(eventType, data, metadata))
			throw new ArgumentException("Record is too big.", nameof(data));

		EventId = eventId;
		EventType = eventType;
		IsJson = isJson;
		Data = data ?? [];
		Metadata = metadata ?? [];
		Properties = properties ?? [];
	}

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata)
		: this(eventId, eventType, isJson, data, metadata, []) {
	}

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata)
		: this(eventId, eventType, isJson, data, metadata, []) {
	}
}
