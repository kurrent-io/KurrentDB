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
	public Guid   EventId            { get; }
	public string EventType          { get; }
	public bool   IsJson             { get; }
	public byte[] Data               { get; }
	public bool   IsPropertyMetadata { get; }
	public byte[] Metadata           { get; }

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata)
		: this(
			eventId, eventType, isJson, Helper.UTF8NoBom.GetBytes(data),
			isPropertyMetadata: false,
			metadata != null ? Helper.UTF8NoBom.GetBytes(metadata) : null) { }

	public Event(Guid eventId, string eventType, bool isJson, byte[] data)
		: this(eventId, eventType, isJson, data, isPropertyMetadata: false, []) { }

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, bool isPropertyMetadata, byte[] metadata) {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Empty eventId provided.", nameof(eventId));
        if (string.IsNullOrEmpty(eventType))
            throw new ArgumentException("Empty eventType provided.", nameof(eventType));
        if (ExceedsMaximumSizeOnDisk(eventType, data, metadata))
            throw new ArgumentException("Record is too big.", nameof(data));

		EventId            = eventId;
		EventType          = eventType;
		IsJson             = isJson;
		Data               = data ?? [];
		IsPropertyMetadata = isPropertyMetadata;
		Metadata           = metadata ?? [];
	}

    /// <summary>
    /// Used in the new append API that takes raw bytes for data and properties.
    /// <remarks>
    /// The ubiquitous language in the codebase is "metadata" for what this constructor calls "properties".
    /// This is to avoid confusion with the "metadata" field in the Event record.
    /// The same goes for recordId vs eventId and schemaName vs eventType.
    /// </remarks>
    /// </summary>
	public Event(Guid recordId, string schemaName, bool isJson, byte[] data, byte[] properties) {
		EventId            = recordId;
		EventType          = schemaName;
		IsJson             = isJson;
		Data               = data;
		IsPropertyMetadata = true;
		Metadata           = properties;
	}

	public static int SizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		(data?.Length ?? 0) + (metadata?.Length ?? 0) + eventType.Length * 2;

	static bool ExceedsMaximumSizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		SizeOnDisk(eventType, data, metadata) > TFConsts.EffectiveMaxLogRecordSize;
}
