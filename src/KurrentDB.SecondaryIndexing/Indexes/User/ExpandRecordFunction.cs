// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Functions;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal sealed class ExpandRecordFunction(Func<long[], ClaimsPrincipal, IEnumerator<ReadResponse>> eventsProvider)
	: GetDatabaseEventsFunction<EventColumns>(Name, eventsProvider) {

	private new const string Name = "get_kdb_usr";
	public static ReadOnlySpan<byte> UnnestExpression => "unnest(get_kdb_usr(log_position))"u8;

	protected override void Bind(BindingContext context) {
		// nothing to initialize here
	}

	protected override void FillRow<TBuilder>(EventRecord ev, ref TBuilder builder, int rowIndex) {
		// Data column
		var column = builder[0];
		if (ev.IsJson) {
			// JSON can be copied to DuckDB directly because it's encoded as UTF-8
			column.SetValue(rowIndex, ev.Data.Span);
		} else {
			WriteBase64(column, rowIndex, ev.Data.Span);
		}

		// Metadata column
		column = builder[1];
		column.SetValue(rowIndex, ev.Metadata.Span is { Length: > 0 } metadata ? metadata : "{}"u8);

		// StreamId column
		column = builder[2];
		column.SetValue(rowIndex, ev.EventStreamId);

		// EventType column
		column = builder[3];
		column.SetValue(rowIndex, ev.EventType);
	}

	protected override void FillRowWithEmptyData<TBuilder>(ref TBuilder builder, int rowIndex) {
		// Data column
		var column = builder[0];
		column.SetValue(rowIndex, "{}"u8);

		// Metadata column
		column = builder[1];
		column.SetValue(rowIndex, "{}"u8);

		// StreamId column
		column = builder[2];
		column.SetValue(rowIndex, string.Empty);

		// EventType column
		column = builder[3];
		column.SetValue(rowIndex, string.Empty);
	}
}

internal readonly ref struct EventColumns : ICompositeReturnType {
	private const DuckDBType Data = DuckDBType.Varchar;
	private const DuckDBType Metadata = DuckDBType.Varchar;
	private const DuckDBType StreamId = DuckDBType.Varchar;
	private const DuckDBType EventType = DuckDBType.Varchar;

	static IReadOnlyList<KeyValuePair<string, LogicalType>> ICompositeReturnType.ReturnType => new ICompositeReturnType.Builder {
		{ Data, "data" },
		{ Metadata, "metadata" },
		{ StreamId, "stream_id" },
		{ EventType, "event_type" },
	};
}
