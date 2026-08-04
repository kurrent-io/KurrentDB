// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Benchmarks.Retrieval;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Serilog;

await RunRetrievalQuality();

return;

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

		var legacy = await benchmark.Run("legacy", KontextRetriever.New().Legacy(corpus.Store, corpus.EmbeddingGenerator).Build());
		var current = await benchmark.Run("default", KontextRetriever.New().Default(corpus.Store, corpus.EmbeddingGenerator).Build());

		QualityReport.PrintMetrics([legacy, current], baseline: legacy);
		QualityReport.PrintHeadToHead(legacy, current);
	}
	finally {
		await Log.CloseAndFlushAsync();
	}
}
