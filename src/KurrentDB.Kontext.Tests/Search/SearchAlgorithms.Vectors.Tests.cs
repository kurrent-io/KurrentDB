// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Search;

public class SearchAlgorithmsVectorsTests {
	[Test]
	public async Task DotProduct_Identical_Normalized_Vectors() {
		var v = new float[] { 0.6f, 0.8f };
		var result = SearchAlgorithms.DotProduct(v, v);
		await Assert.That(result).IsGreaterThanOrEqualTo(0.99f);
	}

	[Test]
	public async Task DotProduct_Orthogonal_Vectors() {
		var a = new float[] { 1, 0 };
		var b = new float[] { 0, 1 };
		await Assert.That(SearchAlgorithms.DotProduct(a, b)).IsEqualTo(0f);
	}

	[Test]
	public async Task DotProduct_Mismatched_Lengths_Throws() {
		var a = new float[] { 1, 2, 3 };
		var b = new float[] { 4, 5 };
		await Assert.That(() => SearchAlgorithms.DotProduct(a, b)).Throws<ArgumentException>();
	}
}