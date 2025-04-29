// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

[StructLayout(LayoutKind.Auto)]
internal readonly partial struct QueryResult : IDisposable {
	private readonly DuckDBResult _result;
	private readonly bool _initialized;

	internal QueryResult(nint preparedStatement) {
		if (ExecutePrepared(preparedStatement, out _result) is DuckDBState.Error) {
			var ex = Interop.CreateException(GetErrorString(in _result), GetErrorType(in _result));
			Destroy(in _result);
			throw ex;
		}

		_initialized = true;
	}

	public long RowsChanged => _initialized ? GetRowsChangedCount(in _result) : 0L;

	public void Dispose() {
		if (_initialized) {
			Destroy(in _result);
		}
	}
}
