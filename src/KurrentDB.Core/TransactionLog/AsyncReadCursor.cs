// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using DotNext.Runtime;
using DotNext.Runtime.InteropServices;

namespace KurrentDB.Core.TransactionLog;

/// <summary>
/// Represents async-friendly cursor.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct AsyncReadCursor : IReadCursor {
	private readonly ValueReference<long> _cursor;

	private AsyncReadCursor(ValueReference<long> cursor) => _cursor = cursor;

	/// <summary>
	/// Gets the position of the cursor.
	/// </summary>
	public ref long Position => ref _cursor.Value;

	long IReadCursor.Position {
		get => Position;
		set => Position = value;
	}

	/// <summary>
	/// Represents the lifetime of the cursor.
	/// </summary>
	[StructLayout(LayoutKind.Auto)]
	public struct Scope(long position) : IDisposable {
		private UnmanagedMemory<long> _cursor = new(position);

		public Scope() : this(position: 0L) { }

		/// <summary>
		/// Gets the cursor.
		/// </summary>
		public readonly AsyncReadCursor Cursor => new(_cursor.Pointer);

		public void Dispose() => _cursor.Dispose();
	}
}
