// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using System.Runtime.InteropServices;
using DotNext.Buffers.Binary;

namespace Kurrent.Quack;

/// <summary>
/// Provides a binding between formal parameters and the actual arguments.
/// </summary>
/// <param name="statement">The binding source.</param>
[StructLayout(LayoutKind.Auto)]
public struct BindingContext(PreparedStatement statement) : IEnumerable<object> {
	private int index = 1; // in DuckDB, the index of the first parameter is 1

	internal readonly int Count => index - 1;

	public void Add(int value) => statement.Bind(index++, value);
	public void Add(uint value) => statement.Bind(index++, value);
	public void Add(long value) => statement.Bind(index++, value);
	public void Add(ulong value) => statement.Bind(index++, value);
	public void Add(DBNull value) => statement.Bind(index++, value);
	public void Add(DateTime value) => statement.Bind(index++, value);
	public void Add(ReadOnlySpan<byte> buffer) => statement.Bind(index++, buffer);
	public void Add(ReadOnlySpan<char> chars) => statement.Bind(index++, chars);
	public void Add(bool value) => statement.Bind(index++, value);
	public void Add(float value) => statement.Bind(index++, value);
	public void Add(double value) => statement.Bind(index++, value);
	public void Add(Int128 value) => statement.Bind(index++, value);
	public void Add(UInt128 value) => statement.Bind(index++, value);

	public void Add<T>(T value)
		where T : struct, IBinaryFormattable<T>
		=> statement.Bind(index++, value);

	public void Add(string? str) {
		if (str is not null) {
			Add(str.AsSpan());
		} else {
			Add(DBNull.Value);
		}
	}

	private static IEnumerator<object> GetEmptyEnumerator() => Enumerable.Empty<object>().GetEnumerator();

	IEnumerator<object> IEnumerable<object>.GetEnumerator() => GetEmptyEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEmptyEnumerator();
}
