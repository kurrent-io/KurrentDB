// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class NounPhraseExtractorTests {
	static NounPhraseExtractor? _extractor;

	[Before(Class)]
	public static async Task SetupPipeline(ClassHookContext context) {
		_extractor = new NounPhraseExtractor(NullLogger<NounPhraseExtractor>.Instance);
		await _extractor.InitializeAsync();
	}

	[Test]
	public async Task ExtractKeywords_Empty_String_Returns_Empty() {
		var result = _extractor!.ExtractKeywords("");
		await Assert.That(result).IsEmpty();
	}

	[Test]
	public async Task ExtractKeywords_Whitespace_Returns_Empty() {
		var result = _extractor!.ExtractKeywords("   ");
		await Assert.That(result).IsEmpty();
	}

	[Test]
	public async Task ExtractKeywords_Simple_Sentence_Extracts_Content_Words() {
		var result = _extractor!.ExtractKeywords("the quick brown fox");
		var joined = string.Join(" ", result);

		// Content words preserved; the determiner "the" dropped.
		await Assert.That(joined).Contains("fox", StringComparison.OrdinalIgnoreCase);
		await Assert.That(result.Any(w => string.Equals(w, "the", StringComparison.OrdinalIgnoreCase))).IsFalse();
	}

	[Test]
	public async Task ExtractKeywords_Strips_Determiners_And_Stopwords() {
		var result = _extractor!.ExtractKeywords("the dog and a cat");
		foreach (var w in result) {
			await Assert.That(string.Equals(w, "the", StringComparison.OrdinalIgnoreCase)).IsFalse();
			await Assert.That(string.Equals(w, "a", StringComparison.OrdinalIgnoreCase)).IsFalse();
			await Assert.That(string.Equals(w, "and", StringComparison.OrdinalIgnoreCase)).IsFalse();
		}
	}

	[Test]
	public async Task ExtractText_Joins_Content_Words_With_Spaces() {
		const string input = "The customer placed an order for a widget";
		var text = _extractor!.ExtractText(input);
		var keywords = _extractor!.ExtractKeywords(input);

		await Assert.That(text).IsEqualTo(string.Join(" ", keywords));
	}

	[Test]
	public async Task ExtractKeywords_Keeps_Repeats() {
		// Repeated words are retained so BM25 sees the full term frequency.
		var result = _extractor!.ExtractKeywords("order order order");
		await Assert.That(result.Count).IsGreaterThan(1);
	}

	[Test]
	public async Task ExtractKeywords_Not_Initialized_Throws() {
		var extractor = new NounPhraseExtractor(NullLogger<NounPhraseExtractor>.Instance);
		await Assert.That(() => extractor.ExtractKeywords("hello")).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task ExtractText_Not_Initialized_Throws() {
		var extractor = new NounPhraseExtractor(NullLogger<NounPhraseExtractor>.Instance);
		await Assert.That(() => extractor.ExtractText("hello")).Throws<InvalidOperationException>();
	}
}