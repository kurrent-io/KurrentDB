// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Benchmarks;
using Benchmarks.Entities;
using Benchmarks.Retrieval;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.GlinerOnnx;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Entities;
using Kurrent.Kontext.Entities.Extraction;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

await (args switch {
	["--determinism", ..]        => RunDeterminism(),
	["--max-tokens-ab", ..]      => RunMaxTokensAb(),
	["--main-ab", ..]            => RunMainAb(),
	["--model", var model, ..]   => RunModelLeg(model),
	["--chains", ..]             => RunChains(),
	["--legs", ..]               => RunLegs(),
	["--entities-ab", ..]        => RunEntitiesAb(),
	["--entities-lab", ..]       => RunEntitiesLab(),
	["--entities-quality", ..]   => RunEntitiesQuality(),
	["--extraction-quality", ..] => RunExtractionQuality(),
	_                            => RunRetrievalQuality(),
});

return;

// Scores extraction against labelled memories, in the shape the neo4j agent-memory extraction
// benchmark uses: per-type precision/recall/F1, micro and macro averaged, beside latency and
// throughput. Compares the label vocabularies and thresholds the ingest pipeline can run with.
static async ValueTask RunExtractionQuality() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		var labels = await EntityExtractionLabels.Load(
			Path.Combine(AppContext.BaseDirectory, "Corpus", "Data", "entity-extraction-labels.json"));

		Log.Information(
			"Labels {SampleId}: {Documents} memories, {Entities} labelled entities",
			labels.SampleId, labels.Documents.Count, labels.ExpectedCount);

		var q8 = GlinerOnnxEntityRecognizer.DefaultModelId;

		var pole = EntityTypes.Canonical;
		var life = EntityTypes.Everyday;

		// One GLiNER pass per label set: span scores dilute as the label prompt grows, so a wide
		// vocabulary may recall more when split across passes.
		var variants = new (string Name, string ModelId, double Threshold, IReadOnlyList<IReadOnlyList<string>> LabelSets, bool Split)[] {
			("poleo t=.5 (legacy)", q8, 0.5, [pole], false),
			("shipped t=.5 nosplit", q8, 0.5, [EntityTypes.ExtractionLabels], false),
			("shipped t=.5", q8, 0.5, [EntityTypes.ExtractionLabels], true),
			("shipped t=.4", q8, 0.4, [EntityTypes.ExtractionLabels], true),
			("shipped t=.35", q8, 0.35, [EntityTypes.ExtractionLabels], true),
			("2pass t=.5", q8, 0.5, [pole, life], true),
			("2pass t=.4", q8, 0.4, [pole, life], true),
			("2pass fp32 t=.4", "gliner-small-fp32", 0.4, [pole, life], true),
		};

		var runs = new List<ExtractionRun>();

		foreach (var (name, modelId, threshold, labelSets, split) in variants) {
			using var recognizer = new GlinerOnnxEntityRecognizer(
				EntityCorpusSeeder.GlinerRegistry(modelId),
				new GlinerOnnxOptions { ModelId = modelId, Threshold = threshold });

			var extractor = new EntityExtractor.Pipeline(
				[.. labelSets.Select(set => new EntityExtractor.Gliner(recognizer, set))],
				NullLogger<EntityExtractor.Pipeline>.Instance,
				new EntityExtractor.PipelineOptions { SplitCoordinatedSpans = split });

			runs.Add(await new EntityExtractionBenchmark(labels).Run(name, extractor));
		}

		EntityExtractionReport.PrintMetrics(runs);
		EntityExtractionReport.PrintDetail(runs.MaxBy(run => run.Untyped.F1)!);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// Scores the resolver directly instead of through its echo in ranking: labelled surface forms
// (real extractor output from the corpus, grouped by hand) go through the production resolver and
// writer, and the entity ids they land on are compared with the labels pairwise. No NER model, no
// retrieval — seconds to run, and it names the wrong and missed merges instead of averaging them
// into an ndcg delta.
static async ValueTask RunEntitiesQuality() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		var labels = await EntitySurfaceForms.Load(
			Path.Combine(AppContext.BaseDirectory, "Corpus", "Data", "entity-surface-forms.json"));

		Log.Information(
			"Labels {SampleId}: {Clusters} clusters, {Forms} surface forms",
			labels.SampleId, labels.Clusters.Count, labels.FormCount);

		// The before/after: Legacy is the cascade as it shipped before the resolution work, so the
		// lexical tier is priced rather than assumed.
		var variants = new (string Name, EntityResolverOptions Options)[] {
			("legacy (exact+vector)", EntityResolverOptions.Legacy),
			("shipped", new EntityResolverOptions()),
		};

		var runs = new List<ResolutionRun>();

		// A fresh store per variant: the catalog a run builds is the catalog it resolves against,
		// so sharing one would let the first variant's merges decide the second's.
		foreach (var (name, options) in variants) {
			await using var store = new KontextStoreFixture();
			await store.InitializeAsync();

			runs.Add(await new EntityResolutionBenchmark(labels).Run(name, store, options));
		}

		EntityResolutionReport.PrintMetrics(runs, labels.Clusters.Count);

		foreach (var run in runs)
			EntityResolutionReport.PrintErrors(run);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// The README's headline table: the shipped default against the legacy baseline.
static async ValueTask RunMainAb() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		await using var corpus = new KontextCorpus();
		await corpus.InitializeAsync();

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);

		var legacy = await benchmark.Run(
			"legacy",
			KontextRetriever.New().Legacy(corpus.Store, corpus.EmbeddingGenerator).Build());

		var shipped = await benchmark.Run(
			"default",
			KontextRetriever.New().Default(corpus.Store, corpus.EmbeddingGenerator).Build());

		QualityReport.PrintMetrics([shipped, legacy], baseline: legacy);
		QualityReport.PrintHeadToHead(legacy, shipped);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// The extraction-config sweep behind the entity leg: each config seeds a fresh corpus store the
// way production ingestion would under that config, then Connected is measured against Focused
// on the same store. Per-question outcomes dump to JSON for offline analysis.
static async ValueTask RunEntitiesLab() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	var dumpDir = Environment.GetEnvironmentVariable("ENTITIES_LAB_DUMP") ?? Path.Combine(Path.GetTempPath(), "entities-lab");
	Directory.CreateDirectory(dumpDir);

	// Extraction recall rises steeply as the threshold drops (see --extraction-quality); the
	// question here is whether the entity leg's guards absorb the extra spans or drown in them.
	var configs = new (string Name, EntityCorpusSeeder.SeedOptions Options)[] {
		("t=0.5", new()),
		("t=0.4", new() { Threshold = 0.4 }),
		("t=0.35", new() { Threshold = 0.35 }),
	};

	try {
		var allRuns = new List<QualityRun>();
		QualityRun? focusedRun = null;

		foreach (var (name, options) in configs) {
			await using var corpus = new KontextCorpus();
			await corpus.InitializeAsync();

			var catalog = await EntityCorpusSeeder.Seed(corpus, options);

			Log.Information("[{Config}] catalog: {Entities} entities, {Aliases} aliases, {Mentions} mentions",
				name, catalog.Entities, catalog.Aliases, catalog.Mentions);

			foreach (var (alias, type, mentions) in catalog.TopEntities)
				Log.Information("  {Mentions,4} mentions  {Type,-14} {Alias}", mentions, type, alias);

			var benchmark = new RetrievalQualityBenchmark(corpus.Data);

			if (focusedRun is null) {
				focusedRun = await benchmark.Run(
					"focused (shipped)",
					KontextRetriever.New().Focused(corpus.Store, corpus.EmbeddingGenerator).Build());
				allRuns.Add(focusedRun);
				QualityReport.Dump(focusedRun, Path.Combine(dumpDir, "focused.json"));
			}

			var connected = await benchmark.Run(
				$"{name} connected",
				KontextRetriever.New().Connected(corpus.Store, corpus.Store, corpus.EmbeddingGenerator).Build());

			allRuns.Add(connected);
			QualityReport.Dump(connected, Path.Combine(dumpDir, $"{name}.json"));
		}

		QualityReport.PrintMetrics(allRuns, baseline: focusedRun!);

		var best = allRuns.Skip(1).MaxBy(run => run.NdcgAt(10))!;
		QualityReport.PrintHeadToHead(focusedRun!, best);

		Log.Information("Per-question dumps in {DumpDir}", dumpDir);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// The shipped A/B: seeds the catalog the way production ingestion does (GLiNER extraction over
// the production label vocabulary, the resolver's full cascade, the production writer), then
// measures the shipped Connected chain against the shipped Focused chain. The permissive row
// shows what the measured constants protect against.
static async ValueTask RunEntitiesAb() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		await using var corpus = new KontextCorpus();
		await corpus.InitializeAsync();

		Log.Information("Corpus {SampleId}: {Memories} memories, {Questions} questions", corpus.Data.SampleId, corpus.MemoryCount, corpus.Questions.Count);

		var catalog = await EntityCorpusSeeder.Seed(corpus);

		Log.Information("Entity catalog: {Entities} entities, {Aliases} aliases, {Mentions} mentions", catalog.Entities, catalog.Aliases, catalog.Mentions);

		foreach (var (alias, type, mentions) in catalog.TopEntities)
			Log.Information("  {Mentions,4} mentions  {Type,-14} {Alias}", mentions, type, alias);

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);
		var runs      = new List<QualityRun>();

		var focused = await benchmark.Run(
			"focused (shipped)",
			KontextRetriever.New().Focused(corpus.Store, corpus.EmbeddingGenerator).Build());

		runs.Add(focused);

		var connected = await benchmark.Run(
			"connected (shipped)",
			KontextRetriever.New().Connected(corpus.Store, corpus.Store, corpus.EmbeddingGenerator).Build());

		runs.Add(connected);

		// The cautionary row: the same leg with every guard off — no candidate cap, every
		// query-named entity scoring, no frequency gate.
		runs.Add(await benchmark.Run(
			"connected permissive",
			KontextRetriever.New()
				.Connected(corpus.Store, corpus.Store, corpus.EmbeddingGenerator, configureEntities: options => {
					options.MaxDocumentFrequencyRatio = 1.0;
					options.MaxCandidates             = int.MaxValue;
					options.ScoreRareEntitiesOnly     = false;
				})
				.Build()));

		QualityReport.PrintMetrics(runs, baseline: focused);
		QualityReport.PrintHeadToHead(focused, connected);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// Isolates the raw retrieval signal: each leg is the search stage ALONE, with no reranker or
// modulator on top, so the numbers answer "how much does each leg find" rather than "how well
// does the shipped chain post-process it". Answers whether a vector-only records index suffices.
static async ValueTask RunLegs() {
	Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Console().CreateLogger();

	try {
		await using var corpus = new KontextCorpus();
		await corpus.InitializeAsync();

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);
		var runs      = new List<QualityRun>();

		runs.Add(await benchmark.Run("vector-only",  Leg(new VectorSearch(corpus.Store, corpus.EmbeddingGenerator))));
		runs.Add(await benchmark.Run("keyword-only", Leg(new KeywordSearch(corpus.Store))));

		foreach (var alpha in (double[])[0.3, 0.5, 0.7])
			runs.Add(await benchmark.Run($"hybrid a={alpha:F1}", Leg(new HybridSearch(corpus.Store, corpus.EmbeddingGenerator, alpha, null))));

		Console.WriteLine();
		Console.WriteLine($"{"leg",-14} {"recall@1",9} {"recall@5",9} {"recall@10",10} {"mrr",8} {"ndcg@10",9} {"mean ms",9}");

		foreach (var run in runs.OrderByDescending(run => run.RecallAt(5)).ThenByDescending(run => run.NdcgAt(10)))
			Console.WriteLine($"{run.Name,-14} {run.RecallAt(1),9:F4} {run.RecallAt(5),9:F4} {run.RecallAt(10),10:F4} {run.Mrr,8:F4} {run.NdcgAt(10),9:F4} {run.MeanMs,9:F1}");

		IKontextRetriever Leg(ISearch search) => KontextRetriever.New().AddSearch(search).Build();
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// Every shipped composition on one corpus build, so the comparison is apples to apples: the
// chains share the same embeddings and the same index layout, and only the pipeline differs.
static async ValueTask RunChains() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Warning()
		.WriteTo.Console()
		.CreateLogger();

	try {
		await using var corpus = new KontextCorpus();
		await corpus.InitializeAsync();

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);
		var runs      = new List<QualityRun>();

		foreach (var (name, chain) in Chains())
			runs.Add(await benchmark.Run(name, chain()));

		// Legacy is the prototype the others replaced — it is the baseline every chain is judged against.
		QualityReport.PrintMetrics(runs, baseline: runs[0]);

		Console.WriteLine();
		Console.WriteLine($"{"chain",-10} {"recall@1",9} {"recall@5",9} {"recall@10",10} {"mrr",8} {"ndcg@10",9}");

		foreach (var run in runs.OrderByDescending(run => run.RecallAt(5)).ThenByDescending(run => run.NdcgAt(10)))
			Console.WriteLine($"{run.Name,-10} {run.RecallAt(1),9:F4} {run.RecallAt(5),9:F4} {run.RecallAt(10),10:F4} {run.Mrr,8:F4} {run.NdcgAt(10),9:F4}");

		IEnumerable<(string Name, Func<IKontextRetriever> Chain)> Chains() {
			yield return ("legacy",  () => KontextRetriever.New().Legacy(corpus.Store, corpus.EmbeddingGenerator).Build());
			yield return ("default", () => KontextRetriever.New().Default(corpus.Store, corpus.EmbeddingGenerator).Build());
			yield return ("hybrid",  () => KontextRetriever.New().Hybrid(corpus.Store, corpus.EmbeddingGenerator).Build());
			yield return ("focused", () => KontextRetriever.New().Focused(corpus.Store, corpus.EmbeddingGenerator).Build());
		}
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// One leg of the model comparison on the shipped Focused chain. It is one model per invocation, not
// an in-process A/B: the store's embedding column is FLOAT[KontextIndexConstants.VectorsDimension]
// and that is a compile-time constant, so corpora of different widths cannot coexist in one build.
// Comparing across widths means one run per model with VectorsDimension set to match — 384 for
// pmm12, 768 for pmpnet, 1024 for bgem3 — which the report's "dim N schema FLOAT[M]" line makes
// visible rather than letting a mismatch poison the vectors.
static async ValueTask RunModelLeg(string model) {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		EmbeddingModelFactory factory = model switch {
			// 384 — the shipped width. "pmm12" is the embedded copy the node runs; the rest are the
			// same weights at other export precisions.
			"pmm12"          => configure => new Pmm12EmbeddingGenerator(configure),
			"pmm12-fp32"     => configure => new Pmm12VariantEmbeddingGenerator(OnnxExport.Fp32, configure),
			"pmm12-fp16"     => configure => new Pmm12VariantEmbeddingGenerator(OnnxExport.Fp16, configure),
			"pmm12-uint8"    => configure => new Pmm12VariantEmbeddingGenerator(OnnxExport.Uint8, configure),
			"pmm12-int8full" => configure => new Pmm12VariantEmbeddingGenerator(OnnxExport.Int8Full, configure),
			"pmm12-q4"       => configure => new Pmm12VariantEmbeddingGenerator(OnnxExport.Q4, configure),
			"e5-small"       => configure => new E5SmallEmbeddingGenerator(configure),

			// 768
			"pmpnet"          => configure => new PmpnetEmbeddingGenerator(OnnxExport.Int8Partial, configure),
			"pmpnet-fp32"     => configure => new PmpnetEmbeddingGenerator(OnnxExport.Fp32, configure),
			"pmpnet-fp16"     => configure => new PmpnetEmbeddingGenerator(OnnxExport.Fp16, configure),
			"pmpnet-uint8"    => configure => new PmpnetEmbeddingGenerator(OnnxExport.Uint8, configure),
			"pmpnet-int8full" => configure => new PmpnetEmbeddingGenerator(OnnxExport.Int8Full, configure),
			"pmpnet-q4"       => configure => new PmpnetEmbeddingGenerator(OnnxExport.Q4, configure),

			// 1024
			"bgem3"  => configure => new BgeM3EmbeddingGenerator(configure),
			"arctic" => configure => new ArcticEmbeddingGenerator(configure),

			_ => throw new ArgumentException(
				$"Unknown model '{model}'. 384: pmm12[-fp32|-fp16|-uint8|-int8full|-q4], e5-small. "
			  + "768: pmpnet[-fp32|-fp16|-uint8|-int8full|-q4]. 1024: bgem3.", nameof(model)),
		};

		await using var corpus = new KontextCorpus(null, factory);

		var started = Stopwatch.GetTimestamp();
		await corpus.InitializeAsync();
		var build = Stopwatch.GetElapsedTime(started);

		// Read the dimension off the model rather than the config, so a silent mismatch with the
		// schema column shows up in the report instead of poisoning the vectors.
		var dimension = (await corpus.EmbeddingGenerator.GenerateAsync(["probe"]))[0].Vector.Length;

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);
		var run = await benchmark.Run(
			$"focused {model}",
			KontextRetriever.New().Focused(corpus.Store, corpus.EmbeddingGenerator).Build());

		QualityReport.PrintMetrics([run], baseline: run);

		Console.WriteLine();
		Console.WriteLine($"model {model}  dim {dimension}  schema FLOAT[{KontextIndexConstants.VectorsDimension}]  corpus {corpus.MemoryCount} memories built in {build.TotalSeconds:F1}s");
		Console.WriteLine($"recall@5 {run.RecallAt(5):F4}  mrr {run.Mrr:F4}  ndcg@10 {run.NdcgAt(10):F4}");
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// pMM12 was trained at max_seq_length 128; the generator runs 512, riding position embeddings
// the model never trained on past token 128. Which setting actually ranks better is an
// empirical question — this mode answers it on the shipped chain.
static async ValueTask RunMaxTokensAb() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		var runs = new List<QualityRun>();

		foreach (var maxTokens in (int[])[128, 512]) {
			await using var corpus = new KontextCorpus(options => options.MaxTokens = maxTokens);
			await corpus.InitializeAsync();

			var benchmark = new RetrievalQualityBenchmark(corpus.Data);

			runs.Add(await benchmark.Run(
				$"focused maxTokens={maxTokens}",
				KontextRetriever.New().Focused(corpus.Store, corpus.EmbeddingGenerator).Build()));
		}

		QualityReport.PrintMetrics(runs, baseline: runs[^1]);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

// Localizes the two-leg wobble: sequential triples per composition separate engine-level
// nondeterminism from concurrency effects; the concurrent pair reintroduces the suite's overlap.
static async ValueTask RunDeterminism() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Information()
		.WriteTo.Console()
		.CreateLogger();

	try {
		await using var corpus = new KontextCorpus();
		await corpus.InitializeAsync();

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);

		foreach (var (name, chain) in Chains()) {
			var runs = new List<QualityRun>();

			for (var attempt = 1; attempt <= 3; attempt++)
				runs.Add(await benchmark.Run($"{name} seq{attempt}", chain()));

			QualityReport.PrintMetrics(runs, baseline: runs[0]);
			QualityReport.PrintHeadToHead(runs[0], runs[1]);
		}

		// Raw engine rows, no pipeline: divergent id sequences put the nondeterminism in the
		// engine's fts scan; stable ones put it in the stages.
		foreach (var question in corpus.Questions.Take(4)) {
			var sequences = new List<string>();

			for (var attempt = 0; attempt < 3; attempt++) {
				var hits = new List<string>();

				await foreach (var hit in corpus.Store.SearchAsync(question.Question, [], new FullTextSearchOptions { K = 30 }))
					hits.Add($"{hit.Memory.MemoryId}:{hit.KeywordScore:F4}");

				sequences.Add(string.Join(" ", hits));
			}

			Console.WriteLine();
			Console.WriteLine($"raw fts [{(sequences.Distinct().Count() == 1 ? "STABLE" : "DIVERGENT")}] {question.Question[..Math.Min(60, question.Question.Length)]}");

			foreach (var sequence in sequences.Distinct())
				Console.WriteLine($"  {sequence[..Math.Min(240, sequence.Length)]}");
		}

		// The suite runs evaluations of the same chain concurrently across tests — reproduce that.
		var concurrent = await Task.WhenAll(
			benchmark.Run("default conc1", Default()).AsTask(),
			benchmark.Run("default conc2", Default()).AsTask());

		QualityReport.PrintMetrics(concurrent, baseline: concurrent[0]);
		QualityReport.PrintHeadToHead(concurrent[0], concurrent[1]);

		IEnumerable<(string Name, Func<IKontextRetriever> Chain)> Chains() {
			yield return ("default", Default);
			yield return ("vector", () => SingleLeg(vectorLeg: true));
			yield return ("keyword", () => SingleLeg(vectorLeg: false));
		}

		IKontextRetriever Default() =>
			KontextRetriever.New().Default(corpus.Store, corpus.EmbeddingGenerator).Build();

		IKontextRetriever SingleLeg(bool vectorLeg) =>
			KontextRetriever.New()
				.AddSearch(vectorLeg
					? new VectorSearch(corpus.Store, corpus.EmbeddingGenerator)
					: new KeywordSearch(corpus.Store))
				.AddStage(Bm25Reranker.Create())
				.AddStage(CognitiveModulator.Create())
				.AddStage(MmrReorderer.Create())
				.Build();
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}

static async ValueTask RunRetrievalQuality() {
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Debug()
		.WriteTo.Console()
		.CreateLogger();

	try {
		await using var corpus = new KontextCorpus();
		await corpus.InitializeAsync();

		Log.Information("Corpus {SampleId}: {Memories} memories, {Questions} questions, asOf {AsOf:O}", corpus.Data.SampleId, corpus.MemoryCount, corpus.Questions.Count, corpus.Data.AsOf);

		var benchmark = new RetrievalQualityBenchmark(corpus.Data);
		var runs      = new List<QualityRun>();

		// Greedy hill-climb over the hybrid chain's knobs: every phase keeps the winner and the
		// next phase tunes on top of it, so the final winner is the COMBINATION of winning knobs.
		// Single-leg hybrid chains measure deterministically, so one run per row is trustworthy.
		var bestAlpha     = 0.50;
		var bestLambda    = (double?)0.7;
		var bestReranker  = (Action<Bm25RerankerOptions>?)null;
		var bestOverfetch = (OverfetchOptions?)null;
		var bestEngine    = (Action<HybridSearchOptions>?)null;

		// The shipped baseline every phase is judged against.
		var shipped = await Measure("a=0.50 shipped", Chain(bestAlpha, bestLambda));
		var best    = shipped;

		// Phase A — alpha fine sweep. The coarse sweep put the keyword-lean region ahead.
		foreach (var alpha in (double[])[0.20, 0.25, 0.30, 0.35, 0.40, 0.45]) {
			var run = await Measure($"a={alpha:F2}", Chain(alpha, bestLambda));

			if (Beats(run, best))
				(best, bestAlpha) = (run, alpha);
		}

		// Phase B — MMR trade-off at the winning alpha. Lambda 0.7 is already measured (phase A);
		// null removes the stage entirely.
		foreach (var lambda in (double?[])[0.5, 0.85, 1.0, null]) {
			var name = lambda is { } value ? $"a={bestAlpha:F2} mmr={value:F2}" : $"a={bestAlpha:F2} no-mmr";
			var run  = await Measure(name, Chain(bestAlpha, lambda));

			if (Beats(run, best))
				(best, bestLambda) = (run, lambda);
		}

		// Phase C — the pool-local BM25 reread's merge weights.
		foreach (var (name, tune) in RerankerVariants()) {
			var run = await Measure($"a={bestAlpha:F2} {name}", Chain(bestAlpha, bestLambda, tune));

			if (Beats(run, best))
				(best, bestReranker) = (run, tune);
		}

		// Phase D — candidate pool size into the reread.
		foreach (var floor in (int[])[20, 60]) {
			var overfetch = new OverfetchOptions { Floor = floor };
			var run       = await Measure($"a={bestAlpha:F2} pool={floor}", Chain(bestAlpha, bestLambda, bestReranker, overfetch));

			if (Beats(run, best))
				(best, bestOverfetch) = (run, overfetch);
		}

		// Phase E — engine knobs. The exact scan bounds how much recall the IVF_HNSW_PQ index
		// eats (256 default partitions on 419 rows); refine_factor is the production dial.
		foreach (var (name, tune) in EngineVariants()) {
			var run = await Measure($"a={bestAlpha:F2} {name}", Chain(bestAlpha, bestLambda, bestReranker, bestOverfetch, tune));

			if (Beats(run, best))
				(best, bestEngine) = (run, tune);
		}

		// Interaction re-check — the greedy pass tuned alpha first, on default knobs; re-sweep
		// its neighborhood under the final combination in case the optimum moved.
		foreach (var alpha in (double[])[bestAlpha - 0.05, bestAlpha + 0.05]) {
			if (alpha is <= 0 or >= 1)
				continue;

			var run = await Measure($"a={alpha:F2} recheck", Chain(alpha, bestLambda, bestReranker, bestOverfetch, bestEngine));

			if (Beats(run, best))
				(best, bestAlpha) = (run, alpha);
		}

		// Phase F — LAST on purpose: swaps the corpus FTS index to the ngram tokenizer, so no
		// row after this one can assume the simple tokenizer.
		SwapContentFtsToNgram();
		await Measure($"a={bestAlpha:F2} ngram-fts", Chain(bestAlpha, bestLambda, bestReranker, bestOverfetch, bestEngine));

		QualityReport.PrintMetrics(runs, baseline: shipped);
		QualityReport.PrintHeadToHead(shipped, best);

		Console.WriteLine();
		Console.WriteLine($"winner: {best.Name}  recall@5 {best.RecallAt(5):F4}  mrr {best.Mrr:F4}  ndcg@10 {best.NdcgAt(10):F4}");

		async ValueTask<QualityRun> Measure(string name, IKontextRetriever retriever) {
			var run = await benchmark.Run(name, retriever);
			runs.Add(run);
			return run;
		}

		IKontextRetriever Chain(
			double alpha,
			double? mmrLambda,
			Action<Bm25RerankerOptions>? reranker = null,
			OverfetchOptions? overfetch = null,
			Action<HybridSearchOptions>? engine = null
		) {
			var builder = KontextRetriever.New()
				.Planner(overfetch ?? new OverfetchOptions(), null)
				.AddSearch(new HybridSearch(corpus.Store, corpus.EmbeddingGenerator, alpha, engine))
				.AddStage(Bm25Reranker.Create(reranker))
				.AddStage(CognitiveModulator.Create());

			if (mmrLambda is { } lambda)
				builder = builder.AddStage(MmrReorderer.Create(options => options.Lambda = lambda));

			return builder.Build();
		}

		// Recall@5 is the mission metric; ndcg@10 breaks ties.
		static bool Beats(QualityRun candidate, QualityRun incumbent) =>
			candidate.RecallAt(5) > incumbent.RecallAt(5)
			|| (candidate.RecallAt(5) == incumbent.RecallAt(5) && candidate.NdcgAt(10) > incumbent.NdcgAt(10));

		static IEnumerable<(string Name, Action<Bm25RerankerOptions> Tune)> RerankerVariants() {
			yield return ("bm25-w=1", options => options.Bm25Weight = 1);
			yield return ("bm25-w=3", options => options.Bm25Weight = 3);
			yield return ("bm25-k=5", options => options.K = 5);
			yield return ("bm25-k=20", options => options.K = 20);
		}

		static IEnumerable<(string Name, Action<HybridSearchOptions> Tune)> EngineVariants() {
			yield return ("exact-scan", options => options.UseIndex = false);
			yield return ("refine=1", options => options.RefineFactor = 1);
			yield return ("refine=8", options => options.RefineFactor = 8);
		}

		// Stemming character trigrams is meaningless — the ngram index runs with stem off.
		void SwapContentFtsToNgram() =>
			corpus.DataSources.Execute(connection => {
				using var command = connection.CreateCommand();
				command.CommandText =
					"""
					CREATE INDEX content_fts ON ldb.main.memories (content) USING INVERTED
					WITH (replace = true, base_tokenizer = 'ngram', stem = false);
					""";
				command.ExecuteNonQuery();
			});
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}
