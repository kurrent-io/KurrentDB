// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Security.Claims;
using DotNext.Buffers;
using DotNext.Buffers.Text;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Functions;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.SecondaryIndexing.Indexes;

internal abstract class GetDatabaseEventsFunction<TReturnType>(string functionName, Func<long[], ClaimsPrincipal, IEnumerator<ReadResponse>> eventsProvider) : ScalarFunction<TReturnType>(functionName)
	where TReturnType : ICompositeReturnType, allows ref struct {
	protected static ReadOnlySpan<byte> EmptyJsonUtf8 => "{}"u8;

	// Accepts log_position
	protected sealed override IReadOnlyList<ParameterDefinition> Parameters => [new(DuckDBType.BigInt)];

	protected sealed override void Execute<TBuilder>(ExecutionContext context, in DataChunk input, ref TBuilder builder) {
		var logPositions = input[0].Int64Rows.ToArray(); // TODO: Remove array allocation

		using var enumerator = eventsProvider.Invoke(logPositions, SystemAccounts.System);

		for (var rowIndex = 0; enumerator.MoveNext(); rowIndex++) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived) {
				FillRow(eventReceived.Event.Event, ref builder, rowIndex);
			} else {
				// We should not leave the builder with uninitialized rows to avoid memory garbage to leak into DuckDB internals
				FillRowWithEmptyData(ref builder, rowIndex);
			}
		}
	}

	protected abstract void FillRow<TBuilder>(EventRecord ev, ref TBuilder builder, int rowIndex)
		where TBuilder : struct, DataChunk.IBuilder;

	protected abstract void FillRowWithEmptyData<TBuilder>(ref TBuilder builder, int rowIndex)
		where TBuilder : struct, DataChunk.IBuilder;

	[MethodImpl(MethodImplOptions.NoInlining)]
	protected static void WriteBase64(DataChunk.ColumnBuilder column, int rowIndex, ReadOnlySpan<byte> data) {
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
