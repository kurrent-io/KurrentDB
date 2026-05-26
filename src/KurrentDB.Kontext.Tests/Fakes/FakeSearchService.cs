// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Text.Json;
using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Tests.Fakes;

public class FakeSearchService : IKontextService {
	readonly List<SearchHit> _searchResults = [];
	readonly List<SearchHit> _recallResults = [];
	readonly List<StreamHit> _streamHits = [];
	readonly List<string> _searchQueries = [];

	public IReadOnlyList<string> SearchQueries => _searchQueries;

	public FakeSearchService AddStreamHit(string name, float score = 1f) {
		_streamHits.Add(new StreamHit(name, score));
		return this;
	}

	public async IAsyncEnumerable<StreamHit> DiscoverStreams(
		string workspace, string? keywords, int limit,
		IndexKind kind = IndexKind.EventsStreams,
		[EnumeratorCancellation] CancellationToken ct = default) {
		_searchQueries.Add(keywords ?? "");
		var n = Math.Min(limit, _streamHits.Count);
		for (var i = 0; i < n; i++)
			yield return _streamHits[i];
		await Task.CompletedTask;
	}

	public FakeSearchService AddSearchResult(string stream, long eventNumber, string eventType,
		long commitPosition = 0, float? score = null, object? data = null) {
		_searchResults.Add(new SearchHit(stream, eventNumber, PreparePosition: commitPosition,
			commitPosition, eventType, Timestamp: default, SerializeData(data), Metadata: default, score ?? 0f));
		return this;
	}

	public FakeSearchService AddRecallResult(string stream, long eventNumber, float? score = null,
		string eventType = "", object? data = null) {
		_recallResults.Add(new SearchHit(stream, eventNumber, PreparePosition: 0,
			CommitPosition: 0, eventType, Timestamp: default, SerializeData(data), Metadata: default, score ?? 0f));
		return this;
	}

	static ReadOnlyMemory<byte> SerializeData(object? data) =>
		data == null ? default : JsonSerializer.SerializeToUtf8Bytes(data);

	public async IAsyncEnumerable<SearchHit> SearchAsync(string workspace, string keywords, string? query = null,
		string? streamFilter = null, string? eventTypeFilter = null,
		[EnumeratorCancellation] CancellationToken ct = default) {
		_searchQueries.Add(keywords);
		foreach (var r in _searchResults)
			yield return r;
		await Task.CompletedTask;
	}

	public async IAsyncEnumerable<SearchHit> RecallAsync(string workspace, string keywords, string? query = null,
		[EnumeratorCancellation] CancellationToken ct = default) {
		_searchQueries.Add(keywords);
		foreach (var r in _recallResults)
			yield return r;
		await Task.CompletedTask;
	}
}