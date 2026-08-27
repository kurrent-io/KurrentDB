// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Tests.Modules.Entities;

/// <summary>
/// The match shape every alias is stored in and every query is compared against. One expression,
/// so what holds here holds in the writer's MERGE, the resolver's stem tier and the entity search.
/// </summary>
public class EntityFolderTests {
	[Test]
	[Arguments("camping trips", "camp trip")]
	[Arguments("Camped Trip!", "camp trip")]
	[Arguments("the camping trip", "camp trip")]
	[Arguments("pottery classes", "potteri class")]
	[Arguments("my grandma's café", "grandma café")]
	[Arguments("Google+ users", "googl user")]
	[Arguments("Dr. Sarah O'Brien-Smith", "dr sarah o brien smith")]
	[Arguments("日本語", "日本語")]
	[Arguments("---", "")]
	[Arguments("", "")]
	public async ValueTask folds_to_the_stored_match_shape(string text, string expected) {
		using var folder = new EntityFolder();

		await Assert.That(folder.Fold(text)).IsEqualTo(expected);
	}

	/// <summary>
	/// The property the catalog relies on: a surface form and its morphological variants land on one
	/// needle, and unrelated forms do not.
	/// </summary>
	[Test]
	public async ValueTask variants_of_one_name_share_a_fold_and_different_names_do_not() {
		using var folder = new EntityFolder();

		await Assert.That(folder.Fold("adoption journey")).IsEqualTo(folder.Fold("The Adoption Journeys"));
		await Assert.That(folder.Fold("farmers market")).IsNotEqualTo(folder.Fold("downtown farmers market"));
	}
}
