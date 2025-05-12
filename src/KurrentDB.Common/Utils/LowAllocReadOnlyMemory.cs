// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KurrentDB.Common.Utils;

// Similar to ReadOnlyMemory<T>.
// Unlike ReadOnlyMemory<T>, this not require allocation of a backing array when containing 1 element.
// Useful when 0 or 1 elements is the common case.
[CollectionBuilder(typeof(LowAllocReadOnlyMemoryBuilder), nameof(LowAllocReadOnlyMemoryBuilder.Create))]
public readonly struct LowAllocReadOnlyMemory<T> {
	private readonly bool _isSingle;
	// todo: consider union
	private readonly T _singleItem;
	private readonly ReadOnlyMemory<T> _items; // for 0 or >1 items

	/// <summary>
	/// Construct from single item.
	/// </summary>
	public LowAllocReadOnlyMemory(T singleItem) {
		_isSingle = true;
		_singleItem = singleItem;
	}

	/// <summary>
	/// Construct from ReadOnlyMemory.
	/// Does not capture the ReadOnlyMemory if it is only a single item. Allows the memory to be released earlier.
	/// </summary>
	public LowAllocReadOnlyMemory(ReadOnlyMemory<T> items) {
		if (items.Span is [var singleItem]) {
			_isSingle = true;
			_singleItem = singleItem;
		} else {
			_isSingle = false;
			_items = items;
		}
	}

	public static implicit operator LowAllocReadOnlyMemory<T>(T[] array) => new(items: array);

	public static LowAllocReadOnlyMemory<T> Empty => default;

	public int Length => _isSingle
		? 1
		: _items.Length;

	public T Single => _isSingle
		? _singleItem
		: throw new InvalidOperationException($"Cannot get single item for collection of length {Length}");

	public ReadOnlySpan<T> Span => _isSingle
		? MemoryMarshal.CreateReadOnlySpan(in _singleItem, 1)
		: _items.Span;

	public ReadOnlySpan<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

	public T[] ToArray() => Span.ToArray();
}

// For collection expressions. Allocates a backing array if necessary.
public static class LowAllocReadOnlyMemoryBuilder {
	public static LowAllocReadOnlyMemory<T> Create<T>(ReadOnlySpan<T> items) =>
		items is [var singleItem]
			? new(singleItem: singleItem)
			: new(items: items.ToArray());
}
