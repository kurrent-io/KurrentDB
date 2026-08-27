// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel.Text;

#pragma warning disable SKEXP0050

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// Every candidate chunker against BOTH shapes Kontext stores: a records payload, which arrives as
/// flattened <c>key: value</c> lines from JsonNormalizer, and a memory, which is prose.
/// </summary>
/// <remarks>
/// <para>The two shapes pull in opposite directions. Flattened JSON has no sentences and no
/// paragraphs — every line is a self-contained fact — so a chunker that respects element boundaries
/// under-fills. Prose has real sentence structure, which is what the semantic and ladder strategies
/// were built for.</para>
/// <para>Window utilisation is the number to watch, not chunk count. Every chunk becomes a vector in
/// the row, and Lance compares the query against ALL of them, so half-empty chunks cost query time
/// forever in exchange for nothing.</para>
/// </remarks>
[Category("Integration")]
[Timeout(300_000)]
public class ChunkerComparisonProbeTests {
	const int Window = 128;

	[Test]
	public async ValueTask compares_every_chunker_on_records_and_memory_shapes(CancellationToken cancellationToken) {
		// Arrange
		using var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();
		var tokenizer = ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();

		var report = new StringBuilder().AppendLine();

		foreach (var (shape, lines) in new[] { ("records (flattened json)", RecordsPayload()), ("memory (prose)", MemoryText()) }) {
			var text  = string.Join('\n', lines);
			var total = tokenizer.CountTokens(text);

			report.AppendLine($"=== {shape} — {lines.Length} lines, {total} tokens, window {Window}");
			report.AppendLine($"{"chunker",-30} {"chunks",7} {"min",5} {"max",5} {"mean",6} {"fill",6} {"over",5}");

			Measure(report, "SK TextChunker", tokenizer, TextChunker.SplitPlainTextParagraphs(
				lines, Window, overlapTokens: 0, chunkHeader: null,
				tokenCounter: chunk => tokenizer.CountTokens(chunk)));

			var options = new IngestionChunkerOptions(tokenizer) { MaxTokensPerChunk = Window, OverlapTokens = 0 };

			Measure(report, "DocumentTokenChunker", tokenizer,
				await Collect(new DocumentTokenChunker(options), Document(lines), cancellationToken));

			// The default percentile (95) and a much looser one, because the cut rule is RELATIVE:
			// it always cuts the top N% of distances, even when every distance is tiny.
			foreach (var percentile in new float?[] { null, 99f })
				Measure(report, $"SemanticSimilarity p{percentile?.ToString() ?? "95"}", tokenizer,
					await Collect(new SemanticSimilarityChunker(generator, options, percentile),
						Document(lines), cancellationToken));

			report.AppendLine();
		}

		Console.WriteLine(report.ToString());

		// Assert — the probe compares; it does not pick a winner. The only hard requirement is the
		// one that made chunking necessary at all.
		await Assert.That(report.Length).IsGreaterThan(0);
	}

	// One "key: value" per line, the shape JsonNormalizer emits. No sentences, no paragraphs.
	static string[] RecordsPayload() => [.. Enumerable.Range(0, 120)
		.Select(i => i == 117
			? "failure reason: the payment was declined because the card expired,"
			: $"campo {i}: Sérgio Silveira pagou o café número {i},")];

	// A long memory: prose, real sentences, the shape an agent retains.
	static string[] MemoryText() => [.. Enumerable.Range(0, 60).SelectMany(i => new[] {
		$"Sérgio prefers the {(i % 2 == 0 ? "morning" : "evening")} sessions because the café near the office is quieter then.",
		$"He mentioned that meeting number {i} ran long and the payment for the invoice was declined once already.",
	})];

	static IngestionDocument Document(string[] lines) {
		var section = new IngestionDocumentSection();

		foreach (var line in lines)
			section.Elements.Add(new IngestionDocumentParagraph(line));

		var document = new IngestionDocument("probe");
		document.Sections.Add(section);

		return document;
	}

	static async ValueTask<List<string>> Collect(
		IngestionChunker<string> chunker, IngestionDocument document, CancellationToken cancellationToken) {
		List<string> chunks = [];

		await foreach (var chunk in chunker.ProcessAsync(document, cancellationToken))
			chunks.Add(chunk.Content);

		return chunks;
	}

	static void Measure(StringBuilder report, string label, Tokenizer tokenizer, IReadOnlyList<string> chunks) {
		if (chunks.Count == 0) {
			report.AppendLine($"{label,-30} {"none",7}");
			return;
		}

		var counts = chunks.Select(chunk => tokenizer.CountTokens(chunk)).ToArray();
		var mean   = counts.Average();

		report.AppendLine(
			$"{label,-30} {chunks.Count,7} {counts.Min(),5} {counts.Max(),5} {mean,6:F0} "
		  + $"{mean / Window,6:P0} {counts.Count(count => count > Window),5}");
	}
}
