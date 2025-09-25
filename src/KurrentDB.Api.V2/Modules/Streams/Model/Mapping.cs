// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Core.Data;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams;

public static class Mapping {
	public static Event MapToEvent(this AppendRecord record) {
		Debug.Assert(Guid.TryParse(record.RecordId, out _), "Record ID should have been validated by now");

		return new(
			recordId: Guid.Parse(record.RecordId),
			schemaName: record.Schema.Name,
			isJson: record.Schema.Format == Protocol.V2.Streams.SchemaFormat.Json,
			data: record.Data.ToByteArray(),
			properties: new Struct { Fields = { record.Properties } }.ToByteArray());
	}
}
