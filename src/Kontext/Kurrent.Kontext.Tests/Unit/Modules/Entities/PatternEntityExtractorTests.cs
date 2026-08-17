// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Extraction;

namespace Kurrent.Kontext.Tests.Unit.Modules.Entities;

[Category("Entities")]
public class PatternEntityExtractorTests {
	readonly PatternEntityExtractor _extractor = new();

	[Test]
	public async ValueTask extracts_urls_without_trailing_punctuation() {
		var result = await _extractor.ExtractAsync("Docs moved to https://docs.kurrent.io/kontext. Check them out.");

		var url = await Assert.That(result.Entities).HasSingleItem();
		await Assert.That(url!.Name).IsEqualTo("https://docs.kurrent.io/kontext");
		await Assert.That(url.FullType).IsEqualTo("OBJECT:URL");
	}

	[Test]
	public async ValueTask extracts_emails_with_positions_that_point_back_into_the_text() {
		const string text = "Reach William at william.chong@kurrent.io for access.";

		var result = await _extractor.ExtractAsync(text);

		var email = await Assert.That(result.Entities).HasSingleItem();
		await Assert.That(email!.Name).IsEqualTo("william.chong@kurrent.io");
		await Assert.That(email.FullType).IsEqualTo("OBJECT:EMAIL");
		await Assert.That(text[email.Start!.Value..email.End!.Value]).IsEqualTo(email.Name);
	}

	[Test]
	public async ValueTask plain_prose_yields_nothing() {
		var result = await _extractor.ExtractAsync("Ada Lovelace wrote the first program.");

		await Assert.That(result.Entities).IsEmpty();
	}
}
