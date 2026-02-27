// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DotNext.Buffers;
using DotNext.Buffers.Text;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Functions;
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

		// Created column (Unix timestamp)
		column = builder[3];
		column.Int64Rows[rowIndex] = new DateTimeOffset(ev.TimeStamp).ToUnixTimeMilliseconds();

		// EventType column
		column = builder[5];
		column.SetValue(rowIndex, ev.EventType);

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void WriteBase64(DataChunk.ColumnBuilder column, int rowIndex, ReadOnlySpan<byte> data) {
			const byte quote = (byte)'"';

			var writer = new BufferWriterSlim<byte>(4096);
			writer.Add(quote);
			var encoder = new Base64Encoder();
			try {
				encoder.EncodeToUtf8(data, ref writer, flush: true);
				writer.Add(quote);
				column.SetValue(rowIndex, writer.WrittenSpan);
			} finally {
				writer.Dispose();
			}
		}
	}

	private static void FillRowWithEmptyData<TBuilder>(ref TBuilder builder, int rowIndex)
		where TBuilder : struct, DataChunk.IBuilder {
		// Data column
		var column = builder[0];
		column.SetValue(rowIndex, "{}"u8);

		// Metadata column
		column = builder[1];
		column.SetValue(rowIndex, "{}"u8);

		// StreamId column
		column = builder[2];
		column.SetValue(rowIndex, string.Empty);

		// Created column (Unix timestamp)
		column = builder[3];
		column.Int64Rows[rowIndex] = 0L;

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
	private const DuckDBType EventType = DuckDBType.Varchar;

	static IReadOnlyList<KeyValuePair<string, LogicalType>> ICompositeReturnType.ReturnType => new ICompositeReturnType.Builder {
		Data,
		Metadata,
		StreamId,
		Created,
		EventType,
	};
}
