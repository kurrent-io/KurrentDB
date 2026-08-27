// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// The labelled surface forms the entity pipeline is scored against: real extractor output from
/// the corpus, grouped by hand into identity clusters. The file's own <c>source</c> and
/// <c>note</c> fields carry its provenance and labelling rules.
/// </summary>
public sealed record EntitySurfaceForms {
	public required string                          SampleId { get; init; }
	public required IReadOnlyList<SurfaceFormCluster> Clusters { get; init; }

	/// <summary>Every labelled form, cluster-major — the order the pipeline resolves them in.</summary>
	public IEnumerable<(SurfaceFormCluster Cluster, string Form)> Forms =>
		Clusters.SelectMany(cluster => cluster.Forms.Select(form => (cluster, form)));

	public int FormCount => Clusters.Sum(cluster => cluster.Forms.Count);

	/// <summary>
	/// Every scored pair: two forms of the same entity type, labelled by whether they name the
	/// same thing. Cross-type pairs are absent on purpose — type-strict resolution can never merge
	/// them, so scoring them would only inflate the numbers.
	/// </summary>
	public IEnumerable<SurfaceFormPair> Pairs {
		get {
			var labelled = Forms.ToList();

			for (var left = 0; left < labelled.Count; left++)
			for (var right = left + 1; right < labelled.Count; right++) {
				var (leftCluster, leftForm)   = labelled[left];
				var (rightCluster, rightForm) = labelled[right];

				if (leftCluster.Type != rightCluster.Type)
					continue;

				yield return new SurfaceFormPair(
					leftCluster.Type, leftForm, rightForm,
					SameEntity: leftCluster.Id == rightCluster.Id);
			}
		}
	}

	public static async ValueTask<EntitySurfaceForms> Load(string path) {
		if (!File.Exists(path))
			throw new FileNotFoundException($"The surface-form labels are committed and must be copied to the output directory; not found at '{path}'.", path);

		await using var file = File.OpenRead(path);

		var labels = await JsonSerializer.DeserializeAsync<EntitySurfaceForms>(file, JsonOptions)
			?? throw new InvalidOperationException($"The surface-form labels at '{path}' deserialized to null.");

		if (labels.Clusters.Count == 0)
			throw new InvalidOperationException($"The surface-form labels at '{path}' carry no clusters.");

		var duplicates = labels.Forms
			.GroupBy(entry => (entry.Cluster.Type, Form: entry.Form.ToLowerInvariant()))
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key.Type}:{group.Key.Form}")
			.ToList();

		// A form in two clusters would be labelled both same-entity and different-entity against
		// the same partner, so the scoring would be self-contradictory.
		if (duplicates.Count > 0)
			throw new InvalidOperationException($"The surface-form labels repeat a form within a type: {string.Join(", ", duplicates)}.");

		return labels;
	}

	static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>One real-world thing and every surface form the extractor produced for it.</summary>
public sealed record SurfaceFormCluster {
	public required string               Id    { get; init; }
	public required string               Type  { get; init; }
	public required IReadOnlyList<string> Forms { get; init; }
}

/// <summary>Two same-type forms and the verdict the resolver is scored against.</summary>
public readonly record struct SurfaceFormPair(string Type, string Left, string Right, bool SameEntity);
