// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using DuckDB.NET.Data;
using DuckDB.NET.Data.DataChunk.Writer;
using KurrentDB.DuckDB;
using KurrentDB.SecondaryIndexing.Indexes.Default;

namespace KurrentDB.SecondaryIndexing.Storage;

[UsedImplicitly]
internal class InFlightSetup(DefaultIndexInFlightRecords inFlightRecords) : IDuckDBSetup {
	[Experimental("DuckDBNET001")]
	public void Execute(DuckDBConnection connection) {
		connection.RegisterTableFunction("inflight", ReadBackwardsResultCallback, ReadInFlightMapperCallback);
	}

	public bool OneTimeOnly => false;

	private static readonly IReadOnlyList<ColumnInfo> ColumnInfos = [
		new("log_position", typeof(long)),
		new("event_type", typeof(string)),
		new("category", typeof(string)),
		new("stream", typeof(string)),
		new("event_number", typeof(long)),
		new("created", typeof(long)),
	];

	private TableFunction ReadBackwardsResultCallback() {
		var records = inFlightRecords.GetInFlightRecords();

		return new(ColumnInfos, records);
	}

	[Experimental("DuckDBNET001")]
	private static void ReadInFlightMapperCallback(object? item, IDuckDBDataWriter[] writers, ulong rowIndex) {
		var record = (InFlightRecord)item!;

		writers[0].WriteValue(record.LogPosition, rowIndex);
		writers[1].WriteValue(record.EventType, rowIndex);
		writers[2].WriteValue(record.Category, rowIndex);
		writers[3].WriteValue(record.StreamName, rowIndex);
		writers[4].WriteValue(record.EventNumber, rowIndex);
		writers[5].WriteValue(record.Created, rowIndex);
	}
}
