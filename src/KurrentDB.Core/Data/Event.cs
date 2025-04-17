// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using DotNext.IO;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.Core.Data;

/// <summary>
/// Represents schema information that includes the schema format, and version identifier.
/// </summary>
public record SchemaInfo(SchemaInfo.SchemaDataFormat SchemaFormat, Guid SchemaVersionId) {
	public static readonly int ByteSize =
		16 // schema version id
		+ 1; // schema format

    public static readonly SchemaInfo None = new(0, Guid.Empty);

    public void Write(int destinationIndex, byte[] buffer) {
        Array.Copy(SchemaVersionId.ToByteArray(), 0, buffer, destinationIndex, 16);
        buffer[destinationIndex + 16] = (byte)SchemaFormat;
    }

    public static SchemaInfo Read(ref SequenceReader reader) {
	    var version = reader.Read(16);
	    var format = (SchemaDataFormat)reader.ReadByte();

	    return new SchemaInfo(format, new Guid(version.FirstSpan));
    }

    public enum SchemaDataFormat {
        Undefined = 0,
        Json      = 1,
        Protobuf  = 2,
        Avro      = 3,
        Bytes     = 4
    }

    public static SchemaDataFormat FormatFromString(string format) {
	    return format switch {
		    "application/json" => SchemaDataFormat.Json,
		    "application/protobuf" => SchemaDataFormat.Protobuf,
		    "application/avro" => SchemaDataFormat.Avro,
		    "application/octet-stream" => SchemaDataFormat.Bytes,
		    _ => SchemaDataFormat.Undefined
	    };
    }

    public static string FormatToString(SchemaDataFormat format) {
	    return format switch {
		    SchemaDataFormat.Json => "application/json",
		    SchemaDataFormat.Protobuf => "application/protobuf",
		    SchemaDataFormat.Avro => "application/avro",
		    SchemaDataFormat.Bytes => "application/octet-stream",
		    _ => "application/octet-stream",
	    };
    }
}

public class Event {
	public readonly Guid EventId;
	public readonly string EventType;
	public readonly bool IsJson;
	public readonly byte[] Data;
	public readonly byte[] Metadata;

    public readonly SchemaInfo MetadataSchemaInfo;
    public readonly SchemaInfo DataSchemaInfo;

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata, SchemaInfo dataSchemaInfo, SchemaInfo metadataSchemaInfo)
		: this(
			eventId, eventType, isJson, Helper.UTF8NoBom.GetBytes(data),
			metadata != null ? Helper.UTF8NoBom.GetBytes(metadata) : null, dataSchemaInfo, metadataSchemaInfo) {
	}

	public static int SizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		(data?.Length ?? 0) + (metadata?.Length ?? 0) + (eventType.Length * 2);

	private static bool ExceedsMaximumSizeOnDisk(string eventType, byte[] data, byte[] metadata) =>
		SizeOnDisk(eventType, data, metadata) > TFConsts.EffectiveMaxLogRecordSize;

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata, SchemaInfo dataSchemaInfo, SchemaInfo metadataSchemaInfo) {
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
        DataSchemaInfo = dataSchemaInfo ?? SchemaInfo.None;
        MetadataSchemaInfo = metadataSchemaInfo ?? SchemaInfo.None;
	}

	public Event(Guid eventId, string eventType, bool isJson, byte[] data, byte[] metadata)
		: this(eventId, eventType, isJson, data, metadata, SchemaInfo.None, SchemaInfo.None) {
	}

	public Event(Guid eventId, string eventType, bool isJson, string data, string metadata)
		: this(eventId, eventType, isJson, data, metadata, SchemaInfo.None, SchemaInfo.None) {
	}
}
