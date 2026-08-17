// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Resolution;

namespace Kurrent.Kontext.Tests.Unit.Modules.Entities;

[Category("Entities")]
public class NameSimilarityTests {
	[Test]
	public async ValueTask identical_names_score_one() =>
		await Assert.That(NameSimilarity.TokenSortRatio("john smith", "john smith")).IsEqualTo(1.0);

	[Test]
	public async ValueTask word_order_washes_out() =>
		await Assert.That(NameSimilarity.TokenSortRatio("smith john", "john smith")).IsEqualTo(1.0);

	[Test]
	public async ValueTask a_typo_costs_proportionally() {
		// "jon smith" vs "john smith": one missing character.
		// indel distance 1 over combined length 19 => 1 - 1/19.
		var expected = 1.0 - 1.0 / 19.0;

		await Assert.That(NameSimilarity.TokenSortRatio("jon smith", "john smith")).IsEqualTo(expected).Within(1e-9);
	}

	[Test]
	public async ValueTask unrelated_names_score_low() =>
		await Assert.That(NameSimilarity.TokenSortRatio("kurrent", "cheesecake")).IsLessThan(0.5);

	[Test]
	public async ValueTask empty_against_empty_is_one_and_empty_against_text_is_zero() {
		await Assert.That(NameSimilarity.Ratio("", "")).IsEqualTo(1.0);
		await Assert.That(NameSimilarity.Ratio("", "kurrent")).IsEqualTo(0.0);
	}
}
