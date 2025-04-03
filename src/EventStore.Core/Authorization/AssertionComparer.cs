// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Reflection;

namespace EventStore.Core.Authorization;

public sealed class AssertionComparer : IComparer<IAssertion> {
	private static readonly MethodInfo OpenTypeComparer =
		new Func<IAssertion, IAssertion, int>(Compare<object>).Method.GetGenericMethodDefinition();

	private AssertionComparer() { }

	public static IComparer<IAssertion> Instance { get; } = new AssertionComparer();

	public int Compare(IAssertion x, IAssertion y) {
		var grant = x.Grant.CompareTo(y.Grant);
		if (grant != 0) return grant * -1;

		var type = Comparer<Type>.Default.Compare(x.GetType(), y.GetType());
		if (type != 0) return type;

		var closed = (Func<IAssertion, IAssertion, int>)OpenTypeComparer.MakeGenericMethod(x.GetType())
			.CreateDelegate(typeof(Func<IAssertion, IAssertion, int>));
		return closed(x, y);
	}

	private static int Compare<T>(IAssertion x, IAssertion y) {
		if (x is IComparable<T> comparable)
			return comparable.CompareTo((T)y);
		throw new NotSupportedException(
			"Assertion classes must implement IComparable<T> where T is the Assertion class");
	}
}
