// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// copied from Kurrent.Surge

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace KurrentDB.SecondaryIndexing.Indexes.Surge;

public enum JsSchemaDefinitionType {
	Undefined = 0,
	Json      = 1,
	Protobuf  = 2,
	Avro      = 3,
	Bytes     = 4
}

public readonly record struct JsSchemaInfo(string Subject, JsSchemaDefinitionType Type);

public readonly record struct JsRecordPosition(string StreamId, long PartitionId, ulong LogPosition);

public readonly record struct JsEventStoreRecord {
	public string           RecordId   { get; init; }
	public JsRecordPosition Position   { get; init; }
	public ulong            SequenceId { get; init; }
	public bool             IsRedacted { get; init; }

	public JsSchemaInfo                SchemaInfo { get; init; }
	public Dictionary<string, string?> Headers    { get; init; }
	public object?                     Value      { get; init; }

	public string StreamId    => Position.StreamId;
	public long   PartitionId => Position.PartitionId;
	public ulong  LogPosition => Position.LogPosition;
}
