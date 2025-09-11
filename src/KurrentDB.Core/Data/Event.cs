// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Text;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.Core.Data;

// Event for writing
public record Event {
	public Guid   EventId            { get; init; }
	public string EventType          { get; init; }
	public bool   IsJson             { get; init; }
	public byte[] Data               { get; init; }
	public bool   IsPropertyMetadata { get; init; }
	public byte[] Metadata           { get; init; }

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata)
		: this(
			eventId, eventType, isJson, Helper.UTF8NoBom.GetBytes(data),
			isPropertyMetadata: false,
			metadata != null ? Helper.UTF8NoBom.GetBytes(metadata) : null) { }

	public Event(Guid eventId, string eventType, bool isJson, byte[] data)
		: this(eventId, eventType, isJson, data, isPropertyMetadata: false, []) { }

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, bool isPropertyMetadata, byte[] metadata) {
		Debug.Assert(eventId != Guid.Empty, "Empty eventId provided.");
		Debug.Assert(!string.IsNullOrEmpty(eventType), "Empty eventType provided.");
		Debug.Assert(!ExceedsMaximumSizeOnDisk(eventType, data, metadata), "Record is too big.");

		EventId            = eventId;
		EventType          = eventType;
		IsJson             = isJson;
		Data               = data ?? [];
		IsPropertyMetadata = isPropertyMetadata;
		Metadata           = metadata ?? [];
	}

	public Event(Guid recordId, string schemaName, bool isJson, byte[] data, byte[] properties) {
		EventId            = recordId;
		EventType          = schemaName;
		IsJson             = isJson;
		Data               = data;
		IsPropertyMetadata = true;
		Metadata           = properties;
	}

	public static int SizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		(data?.Length ?? 0) + (metadata?.Length ?? 0) + (eventType.Length * 2);

	static bool ExceedsMaximumSizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		SizeOnDisk(eventType, data, metadata) > TFConsts.EffectiveMaxLogRecordSize;

	/// <summary>
	/// Calculates the size on disk of the event and indicates if it exceeds the maximum allowed size.
	/// </summary>
	/// <param name="size">
	/// The calculated size on disk of the event.
	/// </param>
	/// <param name="exceeded">
	/// The amount by which the size exceeds the maximum allowed size. If the size does not exceed the limit, this will be zero.
	/// </param>
	public bool CalculateSizeOnDisk(out int size, out int exceeded) {
		size     = Data.Length + Metadata.Length + Encoding.UTF8.GetByteCount(EventType);
		exceeded = size - TFConsts.EffectiveMaxLogRecordSize;
		return exceeded > 0;
	}
}
