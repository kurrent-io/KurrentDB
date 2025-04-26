// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

/// <summary>
/// Represents UTF-8 encoded string or BLOB in DuckDB.
/// </summary>
/// <remarks>
/// The lifetime of the BLOB should not be larger than the lifetime of the data chunk row.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay($"{{{nameof(_blobPointer)}}}")]
public readonly unsafe struct Blob {
	private readonly DuckDBString* _blobPointer;

	internal Blob(DuckDBString* blobPointer) => _blobPointer = blobPointer;

	/// <summary>
	/// Gets a reference to internal DuckDB structure that describes BLOB.
	/// </summary>
	public ref readonly DuckDBString Reference => ref _blobPointer[0];
}
