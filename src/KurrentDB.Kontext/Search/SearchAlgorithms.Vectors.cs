// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

public static partial class SearchAlgorithms {
	public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
		if (a.Length != b.Length)
			throw new ArgumentException(
				$"Vectors must have the same length (got {a.Length} and {b.Length}).");

		float sum = 0;
		for (var i = 0; i < a.Length; i++)
			sum += a[i] * b[i];
		return sum;
	}
}