// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using TUnit.Assertions.Enums;
using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: can retain's `related` be served by the store's FULL-TEXT SearchAsync alone, skipping the
/// embedding generator the hybrid overload needs?
///
/// Three questions, one test each:
/// - does it surface the near-duplicate a caller is about to create?
/// - does it honour "related reports LIVE memories" without the caller filtering?
/// - what does dropping the vector leg cost — the reworded duplicate with no shared words?
///
/// The last one is the point. A probe that only shows the happy path measures nothing.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public class RelatedByFullTextProbeTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask full_text_surfaces_the_near_duplicate_first(CancellationToken cancellationToken) {
		// Arrange — the corpus a retain would land into. Only one row restates the incoming claim.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var store = await MemorySeeding.Seed(dataSources,
			Row("near-duplicate", "the test runner lives at scripts/testing/test-runner.cs"),
			Row("unrelated-1",    "penguins waddle across antarctic ice"),
			Row("unrelated-2",    "the projector checkpoints after the batch lands"),
			Row("unrelated-3",    "giraffes browse the tallest acacia leaves"));

		// The memory the agent is retaining right now.
		var incoming = "tests run only through scripts/testing/test-runner.cs";

		// Act — exactly the call retain would make: content in, no embedding, no tag filter.
		var hits = await store
			.SearchAsync(incoming, [], new FullTextSearchOptions { K = 3 }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Assert — the duplicate ranks first and carries a keyword score to report as `similarity`.
		await Assert.That(hits).IsNotEmpty();
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("near-duplicate");
		await Assert.That(hits[0].KeywordScore).IsNotNull();
		await Assert.That(hits[0].KeywordScore!.Value).IsGreaterThan(0);

		// Hybrid mode is the only one that blends; full-text leaves the other legs unset.
		await Assert.That(hits[0].HybridScore).IsNull();
		await Assert.That(hits[0].VectorDistance).IsNull();
	}

	[Test]
	public async ValueTask full_text_never_returns_a_superseded_memory(CancellationToken cancellationToken) {
		// Arrange — the superseded row is the BEST lexical match, so if it can leak, it will.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var store = await MemorySeeding.Seed(dataSources,
			Row("superseded", "the test runner lives at scripts/testing/test-runner.cs") with {
				IsSuperseded = true,
				SupersededAt = Base.AddHours(1),
				SupersededBy = "live",
			},
			Row("live", "the test runner moved to tools/test-runner.cs"));

		var expectedVisible = new List<string> { "live" };

		// Act
		var hits = await store
			.SearchAsync("test runner scripts/testing/test-runner.cs", [], new FullTextSearchOptions { K = 10 }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Assert — the index excludes it, so retain needs no filtering of its own.
		var ids = hits.Select(hit => hit.Memory.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedVisible, CollectionOrdering.Any);
	}

	[Test]
	public async ValueTask full_text_misses_a_reworded_duplicate_that_shares_no_words(CancellationToken cancellationToken) {
		// Arrange — the cost of dropping the vector leg, stated as a test rather than a caveat.
		// Both rows mean the same thing and share no content token. An earlier draft of this probe
		// kept the subject's name in both, and BM25 matched on that alone — a distinctive proper
		// noun is enough for full-text, which narrows the gap to genuinely reworded prose.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var store = await MemorySeeding.Seed(dataSources,
			Row("semantic-duplicate", "the feline rested upon the rug"),
			Row("lexical-noise",      "deployment pipelines were migrated between regions"));

		var incoming = "a cat sat on a mat";

		// Act
		var hits = await store
			.SearchAsync(incoming, [], new FullTextSearchOptions { K = 5 }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Assert — BM25 cannot see the match, so `related` would report nothing useful and the
		// caller would create the duplicate. This is exactly what the hybrid overload's own doc
		// promises to catch, and the reason full-text-only is a stopgap rather than the answer.
		var found = hits.Any(hit => hit.Memory.MemoryId == "semantic-duplicate");

		await Assert.That(found).IsFalse();
	}

	static MemoryRow Row(string id, string content) =>
		new(id, MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base);
}
