// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.SemanticKernel.Text;

#pragma warning disable SKEXP0050

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// Whether Semantic Kernel bounds the window when it is called the documented way — SplitPlainTextLines
/// first, then SplitPlainTextParagraphs — rather than paragraphs alone.
/// </summary>
/// <remarks>
/// TokenTextChunker currently re-splits anything that comes back oversized. If the two-step call
/// holds the bound by itself, that code is unnecessary and should go: hand-written splitting is
/// exactly what using a library is meant to avoid.
/// </remarks>
[Category("Integration")]
[Timeout(120_000)]
public class SemanticKernelTwoStepProbeTests {
	const int Window = 128;

	[Test]
	public async ValueTask reports_whether_the_two_step_call_bounds_the_window() {
		// Arrange
		using var generator = new Pmm12EmbeddingGenerator();
		var tokenizer = ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();
		TextChunker.TokenCounter counter = text => tokenizer.CountTokens(text);

		foreach (var (label, text, header) in Cases()) {
			// Act — paragraphs only, the way TokenTextChunker called it first.
			var oneStep = TextChunker.SplitPlainTextParagraphs(
				text.Split('\n', StringSplitOptions.RemoveEmptyEntries), Window, 0, header, counter);

			// Act — the documented two-step: bound each line, then combine lines into paragraphs.
			var lines    = TextChunker.SplitPlainTextLines(text, Window, counter);
			var twoStep  = TextChunker.SplitPlainTextParagraphs(lines, Window, 0, header, counter);

			// Assert — reported, not asserted: the point is to learn which call bounds the window.
			Console.WriteLine($"\n{label}   header={(header is null ? "none" : "yes")}");
			Console.WriteLine($"  sk one-step   : {Describe(tokenizer, oneStep)}");
			Console.WriteLine($"  sk two-step   : {Describe(tokenizer, twoStep)}");

			// The DataIngestion chunkers on the same input. Their limit is named MaxTokensPerChunk,
			// which reads like a bound — the same thing SK's parameter name implied before it was
			// measured overshooting, so it is worth checking rather than trusting.
			var options  = new IngestionChunkerOptions(tokenizer) { MaxTokensPerChunk = Window };
			var document = Document(text);

			foreach (var (name, chunker) in new (string, IngestionChunker<string>)[] {
				("di token", new DocumentTokenChunker(options)),
				("di header", new HeaderChunker(options)),
				("di semantic", new SemanticSimilarityChunker(generator, options)),
			})
				Console.WriteLine($"  {name,-13} : {Describe(tokenizer, await Collect(chunker, document))}");
		}

		await Assert.That(true).IsTrue();
	}

	static (string Label, string Text, string? Header)[] Cases() => [
		("prose, no header", Prose(), null),
		("prose, header", Prose(), "Sérgio's café preference\n"),
		("one long line, no punctuation",
			string.Join(' ', Enumerable.Range(0, 400).Select(i => $"valor{i}")), null),
		("flattened json", string.Join('\n', Enumerable.Range(0, 120)
			.Select(i => $"campo {i}: Sérgio Silveira pagou o café número {i},")), null),
	];

	static string Prose() => string.Join('\n', Enumerable.Range(0, 60).SelectMany(i => new[] {
		$"Sérgio pagou o café número {i} e comentou que a reunião correu bem.",
		$"Ele mencionou que o pagamento {i} foi recusado uma vez antes de ser aceite.",
	}));

	static IngestionDocument Document(string text) {
		var section = new IngestionDocumentSection();

		foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			section.Elements.Add(new IngestionDocumentParagraph(line));

		var document = new IngestionDocument("probe");
		document.Sections.Add(section);

		return document;
	}

	static async ValueTask<List<string>> Collect(IngestionChunker<string> chunker, IngestionDocument document) {
		List<string> chunks = [];

		await foreach (var chunk in chunker.ProcessAsync(document))
			chunks.Add(chunk.Content);

		return chunks;
	}

	static string Describe(Tokenizer tokenizer, IReadOnlyList<string> chunks) {
		if (chunks.Count == 0)
			return "  0 chunks";

		var counts = chunks.Select(chunk => tokenizer.CountTokens(chunk)).ToArray();
		var over   = counts.Count(count => count > Window);

		return $"{chunks.Count,3} chunks  max {counts.Max(),4}  mean {counts.Average(),4:F0}  "
		     + $"over {over}" + (over > 0 ? "  <-- EXCEEDS" : "");
	}
}
