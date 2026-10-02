// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace KurrentDB.Core.Authorization;

public sealed class AssertionComparer : IComparer<IAssertion> {
	private AssertionComparer() { }

	public static IComparer<IAssertion> Instance { get; } = new AssertionComparer();

	public int Compare(IAssertion x, IAssertion y) {
		var grant = x.Grant.CompareTo(y.Grant);
		if (grant != 0)
			return grant * -1;

		var type = Comparer<Type>.Default.Compare(x.GetType(), y.GetType());
		if (type != 0)
			return type;

		return x is IComparable<IAssertion> comparable
			? comparable.CompareTo(y)
			: throw new NotSupportedException(
				"Assertion classes must implement IComparable");
	}
}
