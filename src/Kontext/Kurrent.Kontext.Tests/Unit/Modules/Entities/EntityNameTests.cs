// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Extraction;

namespace Kurrent.Kontext.Tests.Unit.Modules.Entities;

[Category("Entities")]
public class EntityNameTests {
	[Test]
	[Arguments("  John   Smith ", "john smith")]
	[Arguments("ACME Corp", "acme corp")]
	[Arguments("kurrent\t\nio", "kurrent io")]
	[Arguments("", "")]
	[Arguments("   ", "")]
	public async ValueTask normalize_trims_collapses_and_lowercases(string input, string expected) =>
		await Assert.That(EntityName.Normalize(input)).IsEqualTo(expected);

	[Test]
	[Arguments("It.", "it")]                              // sentence-final period an NER span drags in
	[Arguments("(Acme)", "acme")]
	[Arguments("Chen?", "chen")]
	[Arguments("Dr. Jane Doe", "dr jane doe")]
	[Arguments("!!!", "")]
	[Arguments("o'brien", "o'brien")]                     // internal marks survive
	[Arguments("at&t", "at&t")]
	[Arguments("https://kurrent.io", "https://kurrent.io")]
	public async ValueTask normalize_strips_punctuation_around_each_word(string input, string expected) =>
		await Assert.That(EntityName.Normalize(input)).IsEqualTo(expected);

	[Test]
	[Arguments("John Smith")]
	[Arguments("Acme Corporation")]
	[Arguments("New York")]
	[Arguments("C8")]
	public async ValueTask real_names_are_valid(string name) =>
		await Assert.That(EntityName.IsValid(name)).IsTrue();

	[Test]
	[Arguments("they")]     // pronoun
	[Arguments("The")]      // article, case-insensitive via normalization
	[Arguments("probably")] // filler
	[Arguments("person")]   // over-generic noun
	[Arguments("x")]        // too short
	[Arguments("42")]       // purely numeric
	[Arguments("3.14, 25%")]// numeric with punctuation
	[Arguments("!!!")]      // punctuation only
	[Arguments("")]         // empty
	[Arguments("It.")]      // stopword the NER span dragged a period onto
	[Arguments("Me.")]
	[Arguments("(they)")]
	public async ValueTask noise_is_invalid(string name) =>
		await Assert.That(EntityName.IsValid(name)).IsFalse();

	[Test]
	public async ValueTask key_separates_same_name_across_types() {
		ExtractedEntity person = new() { Name = "Jordan", Type = EntityTypes.Person };
		ExtractedEntity place  = new() { Name = "Jordan", Type = EntityTypes.Location };

		await Assert.That(person.Key).IsNotEqualTo(place.Key);
	}

	[Test]
	public async ValueTask full_type_includes_subtype_only_when_present() {
		ExtractedEntity bare = new() { Name = "a-repo", Type = EntityTypes.Object };
		ExtractedEntity sub  = new() { Name = "https://kurrent.io", Type = EntityTypes.Object, Subtype = "URL" };

		await Assert.That(bare.FullType).IsEqualTo("OBJECT");
		await Assert.That(sub.FullType).IsEqualTo("OBJECT:URL");
	}
}
