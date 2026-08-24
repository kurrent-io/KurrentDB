// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Memories paired with the entities extraction should find in them. The file's own
/// <c>policy</c> field states what was counted as an entity and what was not — the labelling rule
/// decides the score, so it travels with the data.
/// </summary>
public sealed record EntityExtractionLabels {
	public required string                            SampleId  { get; init; }
	public required string                            Policy    { get; init; }
	public required IReadOnlyList<LabelledDocument>   Documents { get; init; }

	public int ExpectedCount => Documents.Sum(document => document.Expected.Count);

	public static async ValueTask<EntityExtractionLabels> Load(string path) {
		if (!File.Exists(path))
			throw new FileNotFoundException($"The extraction labels are committed and must be copied to the output directory; not found at '{path}'.", path);

		await using var file = File.OpenRead(path);

		var labels = await JsonSerializer.DeserializeAsync<EntityExtractionLabels>(file, JsonOptions)
			?? throw new InvalidOperationException($"The extraction labels at '{path}' deserialized to null.");

		if (labels.Documents.Count == 0)
			throw new InvalidOperationException($"The extraction labels at '{path}' carry no documents.");

		return labels;
	}

	static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>One memory and everything extraction is expected to find in it. An empty list is a real label: find nothing here.</summary>
public sealed record LabelledDocument {
	public required string                          MemoryId { get; init; }
	public required string                          Text     { get; init; }
	public required IReadOnlyList<ExpectedEntity>   Expected { get; init; }
}

/// <summary>
/// One entity the text contains. <see cref="Types"/> lists every defensible type, first canonical:
/// zero-shot labels are genuinely ambiguous, so a typed hit accepts any of them.
/// </summary>
public sealed record ExpectedEntity {
	public required string                Name  { get; init; }
	public required IReadOnlyList<string> Types { get; init; }

	public bool AcceptsType(string type) =>
		Types.Any(accepted => string.Equals(accepted, type, StringComparison.OrdinalIgnoreCase));
}
