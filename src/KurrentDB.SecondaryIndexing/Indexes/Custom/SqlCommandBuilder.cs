// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using System.Text;
using DotNext.Buffers;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public ref struct SqlCommandBuilder(Span<byte> destination) : IEnumerable {
	private SpanWriter<byte> _writer = new(destination);

	public ReadOnlySpan<byte> Command => _writer.WrittenSpan;

	public void Add(ReadOnlySpan<byte> bytes) {
		_writer.Write(bytes);
	}

	public void Add(short value) {
		_writer.TryFormat(value);
	}

	public void Add(int value) {
		_writer.TryFormat(value);
	}

	public void Add(uint value) {
		_writer.TryFormat(value);
	}

	public void Add(long value) {
		_writer.TryFormat(value);
	}

	public void Add(ulong value) {
		_writer.TryFormat(value);
	}

	public void Add(double value) {
		_writer.TryFormat(value);
	}

	public void Add(string value) {
		int required = Encoding.UTF8.GetByteCount(value);

		Span<byte> buffer = stackalloc byte[required];
		Encoding.UTF8.GetBytes(value, buffer);

		_writer.Write(buffer);
	}

	public IEnumerator GetEnumerator() => Enumerable.Empty<object>().GetEnumerator();
}
