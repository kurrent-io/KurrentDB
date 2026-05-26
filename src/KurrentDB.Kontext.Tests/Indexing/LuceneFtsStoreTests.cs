// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Indexing;

public class LuceneFtsStoreTests : IDisposable {
	readonly string _dataPath;
	readonly LuceneFtsStore _store;

	public LuceneFtsStoreTests() {
		_dataPath = Path.Combine(Path.GetTempPath(), $"kurrent_kontext_fts_test_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dataPath);
		_store = new LuceneFtsStore(_dataPath, "test", NullLogger<LuceneFtsStore>.Instance);
	}

	public void Dispose() {
		_store.Dispose();
		if (Directory.Exists(_dataPath))
			Directory.Delete(_dataPath, recursive: true);
	}

	[Test]
	public async Task Add_Then_Search_Finds_Document() {
		_store.Add(1, "order placed", "order placed for widgets");
		_store.Refresh();

		var hits = _store.Search(["widgets"], excludeWords: [], limit: 10).ToList();
		await Assert.That(hits).IsEquivalentTo(new[] { 1ul });
	}

	[Test]
	public async Task Flush_Commits_Everything_And_Never_Constrains_The_Checkpoint() {
		_store.Add(1, "order", "order placed");

		var current = new KurrentDB.Core.Data.TFPos(42, 42);
		await Assert.That(_store.Flush(current, force: false)).IsEqualTo(current);
	}

	[Test]
	public async Task Search_Without_Refresh_Sees_Nothing() {
		// NRT semantics: a doc added but not refreshed shouldn't surface.
		_store.Add(1, "order", "order placed");

		var hits = _store.Search(["order"], excludeWords: [], limit: 10).ToList();
		await Assert.That(hits).IsEmpty();
	}

	[Test]
	public async Task Search_Applies_Exclude_Words_As_MustNot() {
		_store.Add(1, "order placed", "order placed");
		_store.Add(2, "order shipped via fedex", "order shipped via fedex");
		_store.Refresh();

		var hits = _store.Search(["order"], excludeWords: ["shipped"], limit: 10).ToList();
		await Assert.That(hits).IsEquivalentTo(new[] { 1ul });
	}

	[Test]
	public async Task Search_Respects_Limit() {
		for (ulong i = 1; i <= 5; i++)
			_store.Add(i, "order", "order");
		_store.Refresh();

		var hits = _store.Search(["order"], excludeWords: [], limit: 2).ToList();
		await Assert.That(hits.Count).IsEqualTo(2);
	}

	[Test]
	public async Task Search_No_Matches_Returns_Empty() {
		_store.Add(1, "order", "order");
		_store.Refresh();

		var hits = _store.Search(["nonexistent"], excludeWords: [], limit: 10).ToList();
		await Assert.That(hits).IsEmpty();
	}

	[Test]
	public async Task Add_With_ForceUpdate_Replaces_Existing_Document() {
		_store.Add(1, "first", "first");
		_store.Refresh();
		_store.Add(1, "second", "second", forceUpdate: true);
		_store.Refresh();

		// The doc is now under "second" terms; "first" should no longer match.
		await Assert.That(_store.Search(["first"], excludeWords: [], limit: 10).ToList()).IsEmpty();
		await Assert.That(_store.Search(["second"], excludeWords: [], limit: 10).ToList())
			.IsEquivalentTo(new[] { 1ul });
	}

	[Test]
	public async Task Search_Multiple_Keywords_Finds_Documents_Matching_Any() {
		_store.Add(1, "order", "order");
		_store.Add(2, "payment", "payment");
		_store.Add(3, "shipping", "shipping");
		_store.Refresh();

		var hits = _store.Search(["order", "payment"], excludeWords: [], limit: 10).ToList();
		await Assert.That(hits).Contains(1ul);
		await Assert.That(hits).Contains(2ul);
		await Assert.That(hits).DoesNotContain(3ul);
	}
}