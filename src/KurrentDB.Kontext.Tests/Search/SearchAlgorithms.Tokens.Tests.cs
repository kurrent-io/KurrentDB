// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Search;

public class SearchAlgorithmsTokensTests {
	[Test]
	public async Task TokenJaccard_Identical_Sets_Returns_One() {
		var a = new HashSet<string> { "a", "b", "c" };
		var b = new HashSet<string> { "a", "b", "c" };
		await Assert.That(SearchAlgorithms.TokenJaccard(a, b)).IsEqualTo(1f);
	}

	[Test]
	public async Task TokenJaccard_Disjoint_Sets_Returns_Zero() {
		var a = new HashSet<string> { "a", "b" };
		var b = new HashSet<string> { "c", "d" };
		await Assert.That(SearchAlgorithms.TokenJaccard(a, b)).IsEqualTo(0f);
	}

	[Test]
	public async Task TokenJaccard_Partial_Overlap() {
		var a = new HashSet<string> { "a", "b", "c" };
		var b = new HashSet<string> { "b", "c", "d" };
		// |{b,c}| / |{a,b,c,d}| = 2/4 = 0.5
		await Assert.That(SearchAlgorithms.TokenJaccard(a, b)).IsEqualTo(0.5f);
	}

	[Test]
	public async Task TokenJaccard_Subset_Returns_Ratio() {
		var a = new HashSet<string> { "a", "b" };
		var b = new HashSet<string> { "a", "b", "c", "d" };
		// |{a,b}| / |{a,b,c,d}| = 2/4 = 0.5
		await Assert.That(SearchAlgorithms.TokenJaccard(a, b)).IsEqualTo(0.5f);
	}

	[Test]
	public async Task TokenJaccard_Empty_Either_Side_Returns_Zero() {
		var nonEmpty = new HashSet<string> { "a", "b" };
		var empty = new HashSet<string>();
		await Assert.That(SearchAlgorithms.TokenJaccard(empty, nonEmpty)).IsEqualTo(0f);
		await Assert.That(SearchAlgorithms.TokenJaccard(nonEmpty, empty)).IsEqualTo(0f);
		await Assert.That(SearchAlgorithms.TokenJaccard(empty, empty)).IsEqualTo(0f);
	}

	[Test]
	public async Task TokenJaccard_Single_Element_Match() {
		var a = new HashSet<string> { "a" };
		var b = new HashSet<string> { "a" };
		await Assert.That(SearchAlgorithms.TokenJaccard(a, b)).IsEqualTo(1f);
	}
}