// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Functions;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.Core.DuckDB;

internal sealed class GetDatabaseEventsFunction(IPublisher publisher) : ScalarFunction<EventColumns>(Name) {
	private new const string Name = "get_kdb";

	// Accepts log_position
	protected override IReadOnlyList<ParameterDefinition> Parameters => [new(DuckDBType.BigInt)];

	protected override void Bind(BindingContext context) {
		// nothing to initialize here
	}

	protected override void Execute<TBuilder>(ExecutionContext context, in DataChunk input, ref TBuilder builder) {
		var logPositions = input[0].Int64Rows.ToArray(); // TODO: Remove array allocation

		using var enumerator = new Enumerator.ReadLogEventsSync(
			bus: publisher,
			logPositions: logPositions,
			user: SystemAccounts.System);

		for (var rowIndex = 0; enumerator.MoveNext(); rowIndex++) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived) {
				FillRow(eventReceived.Event.Event, ref builder, rowIndex);
			} else {
				// We should not leave the builder with uninitialized rows to avoid memory garbage to leak into DuckDB internals
				FillRowWithEmptyData(ref builder, rowIndex);
			}
		}
	}

	private static void FillRow<TBuilder>(EventRecord ev, ref TBuilder builder, int rowIndex)
		where TBuilder : struct, DataChunk.IBuilder {
		// Data column
		var column = builder[0];
		column.SetValue(rowIndex, GetDataString(ev));

		// Metadata column
		column = builder[1];
		column.SetValue(rowIndex, GetMetadataString(ev));

		// StreamId column
		column = builder[2];
		column.SetValue(rowIndex, ev.EventStreamId);

		// Created column (Unix timestamp)
		column = builder[3];
		column.Int64Rows[rowIndex] = new DateTimeOffset(ev.TimeStamp).ToUnixTimeMilliseconds();

		// CreatedAt column
		column = builder[4];
		column.SetValue(rowIndex, ev.TimeStamp.ToString("u"));

		// EventType column
		column = builder[5];
		column.SetValue(rowIndex, ev.EventType);

		static ReadOnlySpan<char> GetDataString(EventRecord ev) {
			var data = ev.Data;
			return ev.IsJson
				? Helper.UTF8NoBom.GetString(data.Span)
				: System.Convert.ToBase64String(data.Span);
		}

		static ReadOnlySpan<char> GetMetadataString(EventRecord ev) {
			var data = ev.Metadata;
			return data.IsEmpty ? "{}" : Helper.UTF8NoBom.GetString(data.Span);
		}
	}

	private static void FillRowWithEmptyData<TBuilder>(ref TBuilder builder, int rowIndex)
		where TBuilder : struct, DataChunk.IBuilder {
		// Data column
		var column = builder[0];
		column.SetValue(rowIndex, string.Empty);

		// Metadata column
		column = builder[1];
		column.SetValue(rowIndex, string.Empty);

		// StreamId column
		column = builder[2];
		column.SetValue(rowIndex, string.Empty);

		// Created column (Unix timestamp)
		column = builder[3];
		column.Int64Rows[rowIndex] = 0L;

		// CreatedAt column
		column = builder[4];
		column.SetValue(rowIndex, string.Empty);

		// EventType column
		column = builder[5];
		column.SetValue(rowIndex, string.Empty);
	}
}

internal readonly ref struct EventColumns : ICompositeReturnType {
	private const DuckDBType Data = DuckDBType.Varchar;
	private const DuckDBType Metadata = DuckDBType.Varchar;
	private const DuckDBType StreamId = DuckDBType.Varchar;
	private const DuckDBType Created = DuckDBType.Varchar;
	private const DuckDBType CreatedAt = DuckDBType.BigInt;
	private const DuckDBType EventType = DuckDBType.Varchar;

	static IReadOnlyList<KeyValuePair<string, LogicalType>> ICompositeReturnType.ReturnType => new ICompositeReturnType.Builder {
		Data,
		Metadata,
		StreamId,
		Created,
		CreatedAt,
		EventType,
	};
}
