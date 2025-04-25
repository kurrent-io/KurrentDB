// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

/// <summary>
/// Represents the query result.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly partial struct StreamQueryResult : IDisposable {
	private readonly DuckDBResult _result;

	internal StreamQueryResult(nint preparedStatement) {
		if (ExecutePrepared(preparedStatement, out _result) is DuckDBState.Error) {
			var ex = Interop.CreateException(QueryResult.GetErrorString(in _result), QueryResult.GetErrorType(in _result));
			QueryResult.Destroy(in _result);
			ColumnCount = 0L;
			throw ex;
		}

		ColumnCount = GetsColumnCount(in _result);
	}

	public bool TryFetch(out DataChunk chunk) {
		nint chunkPtr;

		if (ColumnCount > 0L && (chunkPtr = Fetch(_result)) is not 0) {
			chunk = new DataChunk(chunkPtr);
			return true;
		}

		chunk = default;
		return false;
	}

	public readonly long ColumnCount { get; }

	public void Dispose() {
		if (ColumnCount > 0L) {
			QueryResult.Destroy(in _result);
		}
	}
}
