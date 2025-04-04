// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;

namespace EventStore.Core.Tests.Helpers;

public static class CollectionsExtensions {
	public static bool ContainsNo<TMessage>(this IEnumerable<object> collection) {
		return collection.ContainsNo<TMessage>(v => true);
	}

	public static bool ContainsNo<TMessage>(this IEnumerable<object> collection, Predicate<TMessage> predicate) {
		return collection.ContainsN<TMessage>(0, predicate);
	}

	public static bool ContainsSingle<TMessage>(this IEnumerable<object> collection) {
		return collection.ContainsSingle<TMessage>(v => true);
	}

	public static bool ContainsSingle<TMessage>(this IEnumerable<object> collection,
		Predicate<TMessage> predicate) {
		return collection.ContainsN<TMessage>(1, predicate);
	}

	public static bool ContainsN<TMessage>(this IEnumerable<object> collection, int n) {
		return collection.ContainsN<TMessage>(n, v => true);
	}

	public static bool ContainsN<TMessage>(this IEnumerable<object> collection, int n,
		Predicate<TMessage> predicate) {
		return collection.OfType<TMessage>().Count(v => predicate(v)) == n;
	}

	public static bool IsEmpty(this IEnumerable<object> collection) {
		return !collection.Any();
	}
}
