// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

	public static implicit operator LowAllocReadOnlyMemory<T>(Memory<T> items) => new(items: items);

	public static implicit operator LowAllocReadOnlyMemory<T>(ImmutableArray<T> array) => new(items: array.AsMemory());

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

	/// <summary>
	/// Helper logic for building LowAllocReadOnlyMemorys
	/// Note that this is an immutable datastructure
	/// </summary>
	public readonly struct Builder {
		private readonly bool _isSingle;
		private readonly T _singleItem;
		private readonly List<T> _items;

		public Builder(T singleItem) {
			_isSingle = true;
			_singleItem = singleItem;
		}

		public Builder(List<T> items) {
			if (items is [var singleItem]) {
				_isSingle = true;
				_singleItem = singleItem;
			} else {
				_isSingle = false;
				_items = items;
			}
		}

		public static Builder Empty => default;

		public int Count => _isSingle
			? 1
			: _items?.Count ?? 0;

		public Builder Add(T item) {
			if (_isSingle) {
				return new([_singleItem, item]);
			} else if (_items is not null) {
				_items.Add(item);
				return new(_items);
			} else {
				return new(item);
			}
		}

		public LowAllocReadOnlyMemory<T> Build() => _isSingle
			? new(_singleItem)
			: _items.ToLowAllocReadOnlyMemory();
	}
}

public static class LowAllocReadOnlyMemoryBuilder {
	// For collection expressions. Allocates a backing array if necessary.
	public static LowAllocReadOnlyMemory<T> Create<T>(ReadOnlySpan<T> items) => items switch {
		[] => LowAllocReadOnlyMemory<T>.Empty,
		[var singleItem] => new(singleItem: singleItem),
		_ => new(items: items.ToArray()),
	};

	public static LowAllocReadOnlyMemory<T> ToLowAllocReadOnlyMemory<T>(this IList<T> items) => items switch {
		null => [],
		[] => [],
		[var singleItem] => new(singleItem: singleItem),
		_ => new(items: items.ToArray()),
	};
}
