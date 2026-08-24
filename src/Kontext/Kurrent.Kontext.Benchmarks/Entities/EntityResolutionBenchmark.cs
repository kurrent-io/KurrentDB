// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Modules.Entities;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Testing;
using Kurrent.Surge;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.AI;
using Contracts = Kurrent.Kontext.Contracts.V3.Entities;

namespace Benchmarks.Entities;

/// <summary>
/// Scores the resolver against labelled surface forms: every form goes through the production
/// resolver and writer one at a time — the same incremental view the resolution module has — and
/// the entity ids they land on are compared with the labels pairwise.
/// <para>Extraction is deliberately out of scope here: the labels ARE extractor output, so this
/// isolates "does the resolver put the same thing together and different things apart" from "does
/// the extractor find it at all". No NER model needed, so it runs in seconds.</para>
/// </summary>
sealed class EntityResolutionBenchmark(EntitySurfaceForms labels) {
	/// <summary>Where one labelled form landed, and which tier put it there.</summary>
	public sealed record Assignment(string EntityId, Contracts.ResolutionMethod Method, double Confidence);

	public async ValueTask<ResolutionRun> Run(string name, KontextStoreFixture store, EntityResolverOptions options) {
		var resolver = new KontextEntityResolver(store.DataSources, store.EmbeddingGenerator, options);

		using var connection = store.DataSources.OpenLanceWriter();

		var writer = new KontextEntityWriter(
			connection, store.EmbeddingGenerator,
			new EmbeddingGenerationOptions { Dimensions = KontextSchemaTask.Dimension });

		var assignments = new Dictionary<(string Type, string Form), Assignment>();
		var position    = 0UL;

		// One form per batch, in label order: a later form resolves against the catalog the
		// earlier ones built, exactly as ingestion sees it memory by memory.
		foreach (var (cluster, form) in labels.Forms) {
			var extracted = new ExtractedEntity(form, cluster.Type, 1.0);
			var key       = EntityKey.For(cluster.Type, form);

			var resolutions = await resolver.ResolveAsync([extracted]);
			var resolved    = resolutions[key];

			assignments[(cluster.Type, form)] = new(resolved.EntityId, resolved.Method, resolved.Confidence);

			var mentioned = new Contracts.EntitiesMentioned {
				MemoryId   = $"form-{position}",
				ResolvedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddMinutes(position)),
				Mentions   = { extracted.ToContract(resolved) },
			};

			await writer.ProjectAsync([Record(mentioned, ++position)]);
		}

		return Score(name, assignments);
	}

	ResolutionRun Score(string name, IReadOnlyDictionary<(string Type, string Form), Assignment> assignments) {
		var verdicts = new List<PairVerdict>();

		foreach (var pair in labels.Pairs) {
			var left  = assignments[(pair.Type, pair.Left)];
			var right = assignments[(pair.Type, pair.Right)];

			verdicts.Add(new(pair, Merged: left.EntityId == right.EntityId, Left: left, Right: right));
		}

		// One cluster landing on several ids is the fragmentation the pairwise recall reflects;
		// counting it directly names WHICH thing came apart.
		var fragmented = labels.Clusters
			.Select(cluster => (
				Cluster: cluster,
				Ids: cluster.Forms.Select(form => assignments[(cluster.Type, form)].EntityId).Distinct().Count()))
			.Where(entry => entry.Ids > 1)
			.Select(entry => (entry.Cluster.Id, entry.Cluster.Type, Pieces: entry.Ids))
			.ToList();

		return new(name, verdicts, assignments, fragmented);
	}


	// The writer switches on Value and never reads Data, so raw proto bytes and a cosmetic
	// SchemaInfo are enough — the same shape the writer suites fabricate.
	static SurgeRecord Record<T>(T message, ulong position) where T : IMessage<T> =>
		new() {
			Id         = Guid.NewGuid(),
			Position   = RecordPosition.ForLog(position),
			Timestamp  = DateTime.UnixEpoch,
			SchemaInfo = new SchemaInfo($"$kontext-{typeof(T).Name.ToLowerInvariant()}", SchemaDataFormat.Json),
			Data       = message.ToByteArray(),
			Value      = message,
			ValueType  = typeof(T),
			SequenceId = position,
			Headers    = new Headers(),
		};
}

sealed record PairVerdict(
	SurfaceFormPair Pair,
	bool Merged,
	EntityResolutionBenchmark.Assignment Left,
	EntityResolutionBenchmark.Assignment Right
) {
	public bool TruePositive  => Merged && Pair.SameEntity;
	public bool FalsePositive => Merged && !Pair.SameEntity;
	public bool FalseNegative => !Merged && Pair.SameEntity;
}

sealed record ResolutionRun(
	string Name,
	IReadOnlyList<PairVerdict> Verdicts,
	IReadOnlyDictionary<(string Type, string Form), EntityResolutionBenchmark.Assignment> Assignments,
	IReadOnlyList<(string Cluster, string Type, int Pieces)> Fragmented
) {
	public int TruePositives  => Verdicts.Count(verdict => verdict.TruePositive);
	public int FalsePositives => Verdicts.Count(verdict => verdict.FalsePositive);
	public int FalseNegatives => Verdicts.Count(verdict => verdict.FalseNegative);

	public int SameEntityPairs => Verdicts.Count(verdict => verdict.Pair.SameEntity);

	/// <summary>Of the merges the resolver made, how many were right — the over-merge guard.</summary>
	public double Precision => TruePositives + FalsePositives is var merged and > 0 ? (double)TruePositives / merged : 1.0;

	/// <summary>Of the merges it should have made, how many it made — the fragmentation guard.</summary>
	public double Recall => SameEntityPairs > 0 ? (double)TruePositives / SameEntityPairs : 1.0;

	public double F1 => Precision + Recall is var sum and > 0 ? 2 * Precision * Recall / sum : 0;

	/// <summary>Entities the catalog ended up holding — one per labelled thing is the ideal.</summary>
	public int DistinctEntities => Assignments.Values.Select(assignment => assignment.EntityId).Distinct().Count();

	public IReadOnlyDictionary<Contracts.ResolutionMethod, int> ByMethod =>
		Assignments.Values
			.GroupBy(assignment => assignment.Method)
			.ToDictionary(group => group.Key, group => group.Count());
}
