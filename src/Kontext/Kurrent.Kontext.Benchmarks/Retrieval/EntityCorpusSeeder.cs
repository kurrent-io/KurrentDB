// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.GlinerOnnx;
using Kurrent.Kontext.Entities;
using Kurrent.Kontext.Entities.Data;
using Kurrent.Kontext.Entities.Extraction;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Contracts = Kurrent.Kontext.Contracts.Entities;

namespace Benchmarks.Retrieval;

/// <summary>
/// Populates the corpus store's entity catalog the way production ingestion does: GLiNER extracts
/// each memory's names, the resolver decides every name's fate, and the production writer merges
/// the resulting events into the catalog tables. Memories are processed in retention order, one
/// batch each, so later memories resolve against the entities earlier ones created — the same
/// incremental view the resolution module has.
/// </summary>
static class EntityCorpusSeeder {
	public sealed record SeededCatalog(int Entities, int Aliases, int Mentions, IReadOnlyList<(string Alias, string Type, int Mentions)> TopEntities);

	/// <summary>The extraction knobs a lab run sweeps; defaults are the production configuration.</summary>
	public sealed record SeedOptions {
		/// <summary>
		/// One GLiNER pass per set. GLiNER's span scores dilute as the label prompt grows, so a
		/// wide vocabulary recalls more when split across passes; the pipeline merges the passes
		/// by surface form. The default is the production extraction vocabulary in one pass.
		/// </summary>
		public IReadOnlyList<IReadOnlyList<string>> LabelSets { get; init; } = [EntityTypes.ExtractionLabels];

		public double Threshold { get; init; } = 0.5;
	}

	public static async ValueTask<SeededCatalog> Seed(KontextCorpus corpus, SeedOptions? options = null, CancellationToken ct = default) {
		options ??= new SeedOptions();

		var modelId = GlinerOnnxEntityRecognizer.DefaultModelId;

		using var recognizer = new GlinerOnnxEntityRecognizer(
			GlinerRegistry(modelId),
			new GlinerOnnxOptions { Threshold = options.Threshold, ModelId = modelId });

		// The Pipeline wrapper, not bare Gliner: SpanFilter (the stopword/validity gate) runs in
		// the pipeline's merge, so bare Gliner would seed pronouns as person entities. GLiNER only —
		// the LLM extractor needs an API key and this benchmark runs fully local.
		var extractor = new EntityExtractor.Pipeline(
			[.. options.LabelSets.Select(labels => new EntityExtractor.Gliner(recognizer, labels))],
			NullLogger<EntityExtractor.Pipeline>.Instance);

		var resolver = new KontextEntityResolver(corpus.DataSources, corpus.EmbeddingGenerator);

		using var connection = corpus.DataSources.OpenLanceWriter();

		var writer = new KontextEntityWriter(
			connection, corpus.EmbeddingGenerator,
			new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension });

		for (var index = 0; index < corpus.Data.Memories.Count; index++) {
			var memory    = corpus.Data.Memories[index];
			var extracted = await extractor.ExtractAsync(memory.Content, ct);

			if (extracted.Count > 0) {
				var resolutions = await resolver.ResolveAsync(extracted, ct);

				var mentioned = new Contracts.EntitiesMentioned {
					MemoryId   = memory.Id,
					ResolvedAt = Timestamp.FromDateTimeOffset(memory.RetainedAt),
					Mentions   = { extracted.Select(entity => entity.ToContract(resolutions[EntityKey.For(entity.EntityType, entity.Text)])) },
				};

				await writer.ApplyAsync([mentioned], ct);
			}

			if ((index + 1) % 100 == 0)
				Log.Information("Entity seeding: {Seeded}/{Total} memories", index + 1, corpus.MemoryCount);
		}

		return Count(corpus);
	}

	static SeededCatalog Count(KontextCorpus corpus) =>
		corpus.DataSources.Execute(connection => {
			using var totals = connection.CreateCommand();
			totals.CommandText =
				"""
				SELECT (SELECT count(DISTINCT entity_id) FROM ldb.main.entities),
				       (SELECT count(*) FROM ldb.main.entities),
				       (SELECT count(*) FROM ldb.main.entity_mentions)
				""";

			int entities, aliases, mentions;

			using (var reader = totals.ExecuteReader()) {
				reader.Read();
				entities = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
				aliases  = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
				mentions = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
			}

			using var top = connection.CreateCommand();
			top.CommandText =
				"""
				SELECT e.alias, e.entity_type, count(*) AS mentions
				FROM ldb.main.entity_mentions m
				JOIN ldb.main.entities e USING (entity_id)
				GROUP BY e.alias, e.entity_type
				ORDER BY mentions DESC
				LIMIT 10
				""";

			var topEntities = new List<(string, string, int)>();

			using (var reader = top.ExecuteReader())
				while (reader.Read())
					topEntities.Add((
						reader.GetString(0),
						reader.GetString(1),
						Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture)));

			return new SeededCatalog(entities, aliases, mentions, topEntities);
		});

	const string GlinerFp32ModelId = "gliner-small-fp32";
	const string GlinerRepoUrl     = "https://huggingface.co/onnx-community/gliner_small-v2.1";

	/// <summary>
	/// The registry a GLiNER leg reads from. The int8 model is one Kontext runs on, so it comes from
	/// KurrentDB.Kontext.Models with the rest of the shipped set — the same copy the integration
	/// tests use. The fp32 export is a benchmark comparison and nothing else loads it, so it is
	/// fetched here when a leg asks for it.
	/// </summary>
	internal static OnnxModelRegistry GlinerRegistry(string modelId) {
		if (modelId == GlinerFp32ModelId) {
			ModelCache.Ensure(BenchmarkModels.Directory(GlinerFp32ModelId), [
				($"{GlinerRepoUrl}/resolve/main/onnx/model.onnx", Path.Combine("onnx", "model.onnx")),
				($"{GlinerRepoUrl}/resolve/main/spm.model", "spm.model"),
			]);

			return new OnnxModelRegistry(BenchmarkModels.Root, [
				new OnnxModelManifest {
					Key     = GlinerFp32ModelId,
					Model   = "model.onnx",
					RepoUrl = GlinerRepoUrl,
					Assets  = ["spm.model"],
				},
			]);
		}

		var modelsDir = Path.GetFullPath(Path.Combine(BenchmarksDir(), "..", "..", "KurrentDB.Kontext.Models"));
		var modelPath = Path.Combine(modelsDir, modelId, "onnx", "model_quantized.onnx");

		if (!File.Exists(modelPath))
			throw new FileNotFoundException(
				$"GLiNER model not found at {modelPath}. Build KurrentDB.Kontext.Models to download it, " +
				"or pass -p:KontextIncludeGliner=true if the download was disabled.", modelPath);

		return new OnnxModelRegistry(modelsDir, [
			new OnnxModelManifest {
				Key     = modelId,
				Model   = "model_quantized.onnx",
				RepoUrl = GlinerRepoUrl,
				Assets  = ["spm.model"],
			},
		]);
	}

	static string BenchmarksDir([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(Path.GetDirectoryName(path))!;
}
