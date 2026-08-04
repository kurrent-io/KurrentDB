// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// The committed corpus file. Data/README.md holds its provenance and transform rules.
/// </summary>
public sealed record CorpusFixture {
	public required string                        SampleId  { get; init; }
	public required DateTimeOffset                AsOf      { get; init; }
	public required IReadOnlyList<CorpusMemory>   Memories  { get; init; }
	public required IReadOnlyList<CorpusQuestion> Questions { get; init; }

	public static async ValueTask<CorpusFixture> Load(string path) {
		if (!File.Exists(path))
			throw new FileNotFoundException($"The corpus fixture is committed and must be copied to the output directory; not found at '{path}'.", path);

		await using var file = File.OpenRead(path);

		var corpus = await JsonSerializer.DeserializeAsync<CorpusFixture>(file, JsonOptions)
			?? throw new InvalidOperationException($"The corpus fixture at '{path}' deserialized to null.");

		if (corpus.Memories.Count == 0 || corpus.Questions.Count == 0)
			throw new InvalidOperationException($"The corpus fixture at '{path}' carries no memories or no questions.");

		return corpus;
	}

	static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>One conversation turn, seeded as one memory under the turn's corpus id.</summary>
public sealed record CorpusMemory {
	public required string         Id         { get; init; }
	public required int            Session    { get; init; }
	public required string         Speaker    { get; init; }
	public required string         Content    { get; init; }
	public required DateTimeOffset RetainedAt { get; init; }
}

/// <summary><see cref="Relevant"/> holds the turn ids cited as evidence — what every metric scores against.</summary>
public sealed record CorpusQuestion {
	public required string                Question { get; init; }
	public required string                Answer   { get; init; }
	public required int                   Category { get; init; }
	public required IReadOnlyList<string> Relevant { get; init; }
}
