// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Benchmarks.Retrieval;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Serilog;

if (args is ["--determinism", ..]) {
	await RunDeterminism();
	return;
}

if (args is ["--max-tokens-ab", ..]) {
	await RunMaxTokensAb();
	return;
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

await RunRetrievalQuality();

return;

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

				await foreach (var hit in corpus.Store.SearchAsync(question.Question, [], new FullTextSearchOptions { Limit = 30, K = 30 }))
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
