// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Serilog;

namespace Benchmarks.Retrieval;

sealed class RetrievalQualityBenchmark(CorpusFixture corpus) {
	public const int Limit = 10;

	public async ValueTask<QualityRun> Run(KontextRetriever retriever) =>
		await Run(retriever.Variant, retriever);

	public async ValueTask<QualityRun> Run(string name, IKontextRetriever retriever) {
		// Warmup, untimed.
		await Retrieve(retriever, corpus.Questions[0]);

		var outcomes = new List<QuestionOutcome>(corpus.Questions.Count);

		foreach (var question in corpus.Questions) {
			var stopwatch = Stopwatch.StartNew();
			var returned  = await Retrieve(retriever, question);
			stopwatch.Stop();

			outcomes.Add(new(question, new(returned, question.Relevant.ToHashSet()), stopwatch.Elapsed));
		}

		Log.Information("Evaluated {Composition}: {Questions} questions", name, outcomes.Count);

		return new(name, outcomes);
	}

	async ValueTask<IReadOnlyList<string>> Retrieve(IKontextRetriever retriever, CorpusQuestion question) {
		// MinScore stays 0: its scale is pipeline-dependent, a cutoff would invalidate the comparison.
		var ranked = await retriever.RetrieveAsync(new() {
			Text  = question.Question,
			Limit = Limit,
			AsOf  = corpus.AsOf,
		});

		return [.. ranked.Select(scored => scored.Memory.MemoryId)];
	}
}
