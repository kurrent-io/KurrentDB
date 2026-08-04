// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Planning;

[Category("Planning")]
public class SynonymQueryExpanderTests {
	static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms = new Dictionary<string, IReadOnlyList<string>> {
		["db"]   = ["database"],
		["auth"] = ["authentication", "login"],
	};

	[Test]
	public async ValueTask appends_known_synonyms_after_each_matching_term_preserving_order() {
		var expander = new SynonymQueryExpander(Synonyms);

		var expanded = await expander.ExpandAsync("db auth speed");

		await Assert.That(expanded).IsEqualTo("db database auth authentication login speed");
	}

	[Test]
	public async ValueTask synonym_already_present_in_the_query_is_not_appended_twice() {
		var expander = new SynonymQueryExpander(Synonyms);

		// "database" already follows "db" in the query, so the db->database synonym must not repeat it.
		var expanded = await expander.ExpandAsync("db database");

		await Assert.That(expanded).IsEqualTo("db database");
	}

	[Test]
	public async ValueTask repeated_term_differing_only_by_case_is_dropped() {
		var expander = new SynonymQueryExpander(Synonyms);

		// none of these casings are synonym keys, so this isolates the `seen` set's own
		// case-insensitivity from dictionary lookup: OrdinalIgnoreCase collapses all three to
		// the first casing encountered.
		var expanded = await expander.ExpandAsync("Rivers RIVERS rivers");

		await Assert.That(expanded).IsEqualTo("Rivers");
	}

	[Test]
	public async ValueTask term_with_no_synonyms_passes_through_untouched() {
		var expander = new SynonymQueryExpander(Synonyms);

		var expanded = await expander.ExpandAsync("hello world");

		await Assert.That(expanded).IsEqualTo("hello world");
	}

	[Test]
	public async ValueTask empty_query_expands_to_an_empty_string() {
		var expander = new SynonymQueryExpander(Synonyms);

		var expanded = await expander.ExpandAsync("");

		await Assert.That(expanded).IsEqualTo("");
	}

	[Test]
	public async ValueTask whitespace_only_query_expands_to_an_empty_string() {
		var expander = new SynonymQueryExpander(Synonyms);

		var expanded = await expander.ExpandAsync("   \t  ");

		await Assert.That(expanded).IsEqualTo("");
	}

	[Test]
	public async ValueTask expansion_is_deterministic_across_repeated_calls() {
		var expander = new SynonymQueryExpander(Synonyms);

		var first  = await expander.ExpandAsync("db auth speed");
		var second = await expander.ExpandAsync("db auth speed");

		await Assert.That(second).IsEqualTo(first);
	}
}
