// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;

namespace KurrentDB.Api.Infrastructure;

/// <summary>
/// A collection that maintains unique elements with index access and preserves insertion order.
/// Combines the uniqueness of HashSet with the index access of List.
/// </summary>
/// <typeparam name="T">The type of elements in the set</typeparam>
[PublicAPI]
public class IndexedSet<T> : IReadOnlyList<T> {
	readonly List<T>    _list;
	readonly HashSet<T> _set;

	public IndexedSet() {
		_list = [];
		_set  = [];
	}

	public IndexedSet(IEqualityComparer<T> comparer) {
		_list = [];
		_set  = new(comparer);
	}

	public IndexedSet(IEnumerable<T> collection) : this() {
		ArgumentNullException.ThrowIfNull(collection);
		foreach (var item in collection) Add(item);
	}

	public IndexedSet(IEnumerable<T> collection, IEqualityComparer<T> comparer) : this(comparer) {
		ArgumentNullException.ThrowIfNull(collection);
		foreach (var item in collection) Add(item);
	}

	public override string ToString() =>
		$"IndexedSet<{typeof(T).Name}>[{Count}] {{ {string.Join(", ", _list.Take(3))}{(Count > 3 ? "..." : "")} }}";

	/// <summary>
	/// Gets the number of elements in the IndexedSet
	/// </summary>
	public int Count => _list.Count;

	/// <summary>
	/// Gets or sets the element at the specified index
	/// </summary>
	/// <param name="index">The zero-based index of the element</param>
	/// <returns>The element at the specified index</returns>
	public T this[int index] => _list[index];

	/// <summary>
	/// Adds an element to the IndexedSet
	/// </summary>
	/// <param name="item">The element to add</param>
	/// <returns>true if the element was added; false if it already exists</returns>
	public bool Add(T item) {
		if (!_set.Add(item)) return false;
		_list.Add(item);
		return true;
	}

	/// <summary>
	/// Removes an element from the IndexedSet
	/// </summary>
	/// <param name="item">The element to remove</param>
	/// <returns>true if the element was removed; false if it was not found</returns>
	public bool Remove(T item) {
		if (!_set.Remove(item)) return false;
		_list.Remove(item); // O(n) operation - this is the trade-off
		return true;
	}

	/// <summary>
	/// Removes the element at the specified index
	/// </summary>
	/// <param name="index">The zero-based index of the element to remove</param>
	public void RemoveAt(int index) {
		var item = _list[index];
		_list.RemoveAt(index);
		_set.Remove(item);
	}

	/// <summary>
	/// Determines whether the IndexedSet contains a specific element
	/// </summary>
	/// <param name="item">The element to locate</param>
	/// <returns>true if the element is found; otherwise, false</returns>
	public bool Contains(T item) => _set.Contains(item);

	/// <summary>
	/// Searches for the specified element and returns the zero-based index of the first occurrence
	/// </summary>
	/// <param name="item">The element to locate</param>
	/// <returns>The zero-based index of the first occurrence of item, or -1 if not found</returns>
	public int IndexOf(T item) => Contains(item) ? _list.IndexOf(item) : -1;

	/// <summary>
	/// Removes all elements from the IndexedSet
	/// </summary>
	public void Clear() {
		_list.Clear();
		_set.Clear();
	}

	/// <summary>
	/// Copies the elements of the IndexedSet to an Array, starting at a particular Array index
	/// </summary>
	/// <param name="array">The one-dimensional Array that is the destination</param>
	/// <param name="arrayIndex">The zero-based index in array at which copying begins</param>
	public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

	/// <summary>
	/// Inserts an element at the specified index, but only if it doesn't already exist
	/// </summary>
	/// <param name="index">The zero-based index at which to insert the element</param>
	/// <param name="item">The element to insert</param>
	/// <returns>true if the element was inserted; false if it already exists</returns>
	public bool Insert(int index, T item) {
		if (index < 0 || index > _list.Count)
			throw new ArgumentOutOfRangeException(nameof(index));

		if (!_set.Add(item)) return false;
		_list.Insert(index, item);
		return true;
	}

	/// <summary>
	/// Returns the elements as a List (creates a copy)
	/// </summary>
	/// <returns>A new List containing all elements</returns>
	public List<T> ToList() => [.._list];

	/// <summary>
	/// Returns the elements as an array
	/// </summary>
	/// <returns>An array containing all elements</returns>
	public T[] ToArray() => _list.ToArray();

	/// <summary>
	/// Determines whether the IndexedSet is a subset of the specified collection
	/// </summary>
	/// <param name="other">The collection to compare to</param>
	/// <returns>true if the IndexedSet is a subset of other; otherwise, false</returns>
	public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);

	/// <summary>
	/// Determines whether the IndexedSet is a superset of the specified collection
	/// </summary>
	/// <param name="other">The collection to compare to</param>
	/// <returns>true if the IndexedSet is a superset of other; otherwise, false</returns>
	public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);

	/// <summary>
	/// Modifies the IndexedSet to contain only elements that are present in both collections
	/// </summary>
	/// <param name="other">The collection to compare to</param>
	public void IntersectWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);
		var otherSet = new HashSet<T>(other);
		var itemsToRemove = _list.Where(item => !otherSet.Contains(item)).ToArray();
		foreach (var item in itemsToRemove) Remove(item);
	}

	/// <summary>
	/// Modifies the IndexedSet to contain all elements that are present in either collection
	/// </summary>
	/// <param name="other">The collection to union with</param>
	public void UnionWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);
		foreach (var item in other) Add(item);
	}

	public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
