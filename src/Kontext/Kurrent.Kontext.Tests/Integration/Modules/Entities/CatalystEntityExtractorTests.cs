// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Extraction;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities;

/// <summary>
/// Runs the REAL WikiNER model. Not a quality benchmark — just proof the model loads, maps to
/// POLE+O types, and reports usable positions.
/// </summary>
[Category("Integration")]
[Category("Entities")]
public class CatalystEntityExtractorTests {
	[Test]
	public async ValueTask recognizes_people_organizations_and_locations() {
		var extractor = await CatalystEntityExtractor.CreateAsync();

		var result = await extractor.ExtractAsync("Satya Nadella runs Microsoft from Redmond.");

		var types = result.Entities.Select(entity => entity.Type).ToHashSet();

		await Assert.That(types).Contains(EntityTypes.Person);
		await Assert.That(types).Contains(EntityTypes.Organization);
		await Assert.That(types).Contains(EntityTypes.Location);
	}

	[Test]
	public async ValueTask positions_point_back_into_the_source_text() {
		const string text = "Satya Nadella runs Microsoft from Redmond.";

		var extractor = await CatalystEntityExtractor.CreateAsync();

		var result = await extractor.ExtractAsync(text);

		await Assert.That(result.Entities).IsNotEmpty();

		foreach (var entity in result.Entities)
			await Assert.That(text[entity.Start!.Value..entity.End!.Value]).IsEqualTo(entity.Name);
	}
}
