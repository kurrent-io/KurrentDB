// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Index;

public static class SortedListExtensions {
	/// <summary>
	/// Returns the index of smallest (according to comparer) element greater than or equal to provided key.
	/// Returns -1 if all keys are smaller than provided key.
	/// </summary>
	public static int LowerBound<TKey, TValue>(this SortedList<TKey, TValue> list, TKey key) {
		if (list.Count == 0)
			return -1;

		var comparer = list.Comparer;
		if (comparer.Compare(list.Keys[list.Keys.Count - 1], key) < 0)
			return -1; // if all elements are smaller, then no lower bound

		int l = 0;
		int r = list.Count - 1;
		while (l < r) {
			int m = l + (r - l) / 2;
			if (comparer.Compare(list.Keys[m], key) >= 0)
				r = m;
			else
				l = m + 1;
		}

		return r;
	}

	/// <summary>
	/// Returns the index of largest (according to comparer) element less than or equal to provided key.
	/// Returns -1 if all keys are greater than provided key.
	/// </summary>
	public static int UpperBound<TKey, TValue>(
		this SortedList<TKey, TValue> list,
		TKey key,
		IComparer<TKey> comparer = null,
		Func<TKey, bool> continueSearch = null) {

		if (continueSearch == null)
			continueSearch = _ => true;

		if (comparer == null)
			comparer = list.Comparer;

		if (list.Count == 0)
			return -1;

		if (!continueSearch(list.Keys[0]))
			throw new SearchStoppedException();

		if (comparer.Compare(key, list.Keys[0]) < 0)
			return -1; // if all elements are greater, then no upper bound

		int l = 0;
		int r = list.Count - 1;
		while (l < r) {
			int m = l + (r - l + 1) / 2;
			if (!continueSearch(list.Keys[m]))
				throw new SearchStoppedException();

			if (comparer.Compare(list.Keys[m], key) <= 0)
				l = m;
			else
				r = m - 1;
		}

		if (!continueSearch(list.Keys[l]))
			throw new SearchStoppedException();

		return l;
	}

	public static async ValueTask<int> UpperBound<TKey, TValue>(
		this SortedList<TKey, TValue> list,
		TKey key,
		IComparer<TKey> comparer,
		Func<TKey, CancellationToken, ValueTask<bool>> continueSearch,
		CancellationToken token) {

		if (list.Count is 0)
			return -1;

		if (!await continueSearch(list.Keys[0], token))
			throw new SearchStoppedException();

		if (comparer.Compare(key, list.Keys[0]) < 0)
			return -1; // if all elements are greater, then no upper bound

		int l = 0;
		int r = list.Count - 1;
		while (l < r) {
			int m = l + (r - l + 1) / 2;
			if (!await continueSearch(list.Keys[m], token))
				throw new SearchStoppedException();

			if (comparer.Compare(list.Keys[m], key) <= 0)
				l = m;
			else
				r = m - 1;
		}

		if (!await continueSearch(list.Keys[l], token))
			throw new SearchStoppedException();

		return l;
	}

	/// <summary>
	/// Returns the index of largest (according to comparer) element that matches the predicate.
	/// Returns -1 if none of the keys match the predicate.
	/// </summary>
	public static async ValueTask<int> FindMax<TKey, TValue>(
		this SortedList<TKey, TValue> list,
		Func<TKey, CancellationToken, ValueTask<bool>> predicate,
		CancellationToken token) {

		if (list.Count is 0)
			return -1;

		int maxIdx = -1;

		for (int i = 0; i < list.Keys.Count; i++) {
			if (!await predicate(list.Keys[i], token))
				continue;

			if (maxIdx is -1 || list.Comparer.Compare(list.Keys[i], list.Keys[maxIdx]) > 0)
				maxIdx = i;
		}

		return maxIdx;
	}
}

public class SearchStoppedException : Exception;
