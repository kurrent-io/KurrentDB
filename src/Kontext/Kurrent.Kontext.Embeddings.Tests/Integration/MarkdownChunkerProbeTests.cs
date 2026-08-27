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
/// The third shape: a structured Markdown document, with headers and sections. This is what
/// <c>HeaderChunker</c> was built for — it "preserves the header context" — and the earlier
/// comparison never fed it one, so its poor showing there says nothing about this case.
/// </summary>
/// <remarks>
/// The measure that matters changes with the shape. For flattened records it is window utilisation,
/// because every half-empty chunk is a vector the query pays for and learns little from. For a
/// document it is whether a chunk still says what it is ABOUT once detached from its section — a
/// paragraph under "## Rollback procedure" that no longer mentions rollback is unfindable, however
/// well the window was packed.
/// </remarks>
[Category("Integration")]
[Timeout(300_000)]
public class MarkdownChunkerProbeTests {
	const int Window = 128;

	[Test]
	public async ValueTask compares_chunkers_on_a_structured_markdown_document(CancellationToken cancellationToken) {
		// Arrange
		using var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();
		var tokenizer = ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();

		var document = MarkdownDocument();
		var lines    = document.SelectMany(section => section.Body.Prepend(section.Header)).ToArray();
		var options  = new IngestionChunkerOptions(tokenizer) { MaxTokensPerChunk = Window, OverlapTokens = 0 };

		var report = new StringBuilder().AppendLine()
			.AppendLine($"=== markdown document — {document.Length} sections, "
			          + $"{tokenizer.CountTokens(string.Join('\n', lines))} tokens, window {Window}")
			.AppendLine($"{"chunker",-26} {"chunks",7} {"mean",6} {"fill",6} {"hdr-ctx",8}");

		// Act
		Measure(report, "SK TextChunker", tokenizer, document,
			TextChunker.SplitPlainTextParagraphs(lines, Window, overlapTokens: 0, chunkHeader: null,
				tokenCounter: chunk => tokenizer.CountTokens(chunk)));

		Measure(report, "HeaderChunker", tokenizer, document,
			await Collect(new HeaderChunker(options), Ingest(document), cancellationToken));

		Measure(report, "SectionChunker", tokenizer, document,
			await Collect(new SectionChunker(options), Ingest(document), cancellationToken));

		Measure(report, "DocumentTokenChunker", tokenizer, document,
			await Collect(new DocumentTokenChunker(options), Ingest(document), cancellationToken));

		Measure(report, "SemanticSimilarity", tokenizer, document,
			await Collect(new SemanticSimilarityChunker(generator, options), Ingest(document), cancellationToken));

		Console.WriteLine(report.ToString());

		await Assert.That(report.Length).IsGreaterThan(0);
	}

	// Sections whose bodies deliberately do NOT repeat their own topic word, so a chunk that loses
	// its header becomes unfindable by that word. That is the property header-aware chunking claims.
	static (string Header, string[] Body)[] MarkdownDocument() => [
		("## Rollback procedure", [.. Enumerable.Range(0, 12).Select(i =>
			$"Step {i}: stop the node, restore the previous snapshot, then verify the checkpoint matches.")]),
		("## Scavenge tuning", [.. Enumerable.Range(0, 12).Select(i =>
			$"Setting {i}: raise the threshold so the background pass runs less often on quiet clusters.")]),
		("## Certificate rotation", [.. Enumerable.Range(0, 12).Select(i =>
			$"Task {i}: replace the key pair, reload the trust store, and confirm the handshake succeeds.")]),
	];

	static IngestionDocument Ingest((string Header, string[] Body)[] sections) {
		var document = new IngestionDocument("markdown-probe");

		foreach (var (header, body) in sections) {
			var section = new IngestionDocumentSection(header);

			// The header has to be an ELEMENT, not the section's markdown. HeaderChunker looks for
			// IngestionDocumentHeader instances; a section that merely carries header text in its own
			// markdown reads to it as headerless, and it falls through to the default path — which is
			// what made four different chunkers emit byte-identical output.
			section.Elements.Add(new IngestionDocumentHeader(header) { Level = 2 });

			foreach (var line in body)
				section.Elements.Add(new IngestionDocumentParagraph(line));

			document.Sections.Add(section);
		}

		return document;
	}

	static async ValueTask<List<string>> Collect(
		IngestionChunker<string> chunker, IngestionDocument document, CancellationToken cancellationToken) {
		List<string> chunks = [];

		await foreach (var chunk in chunker.ProcessAsync(document, cancellationToken))
			chunks.Add(chunk.Content);

		return chunks;
	}

	static void Measure(StringBuilder report, string label, Tokenizer tokenizer,
		(string Header, string[] Body)[] sections, IReadOnlyList<string> chunks) {
		if (chunks.Count == 0) {
			report.AppendLine($"{label,-26} {"none",7}");
			return;
		}

		var counts = chunks.Select(chunk => tokenizer.CountTokens(chunk)).ToArray();
		var mean   = counts.Average();

		// Does each chunk still carry the header of the section it came from? A body line names its
		// own topic nowhere, so this only passes if the chunker kept the header attached.
		var topics    = sections.Select(section => section.Header.Trim('#', ' ')).ToArray();
		var withTopic = chunks.Count(chunk => topics.Any(topic =>
			chunk.Contains(topic, StringComparison.OrdinalIgnoreCase)));

		report.AppendLine($"{label,-26} {chunks.Count,7} {mean,6:F0} {mean / Window,6:P0} "
		                + $"{(double)withTopic / chunks.Count,8:P0}");
	}
}
