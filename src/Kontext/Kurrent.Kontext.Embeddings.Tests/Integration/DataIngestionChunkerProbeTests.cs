// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// The chunkers in Microsoft.Extensions.DataIngestion, measured rather than read about:
/// <c>DocumentTokenChunker</c> (fixed token windows) and <c>SemanticSimilarityChunker</c> (boundaries
/// chosen by cosine distance between neighbouring elements).
/// </summary>
/// <remarks>
/// <para>Two things are being established. First, whether an <see cref="IngestionDocument"/> can be
/// built from flattened JSON at all — the whole library is documented as Markdown-centric and fed by
/// file readers, so a records payload has no obvious entry point. Second, whether the chunks respect
/// the model window, which is the only property that matters for us.</para>
/// <para><c>SemanticSimilarityChunker</c> takes an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// and embeds the elements to decide where to cut. That is a real cost on the records path, which
/// today makes ONE model call per batch, so the probe reports its element count and timing rather
/// than assuming either way.</para>
/// </remarks>
[Category("Integration")]
[Timeout(180_000)]
public class DataIngestionChunkerProbeTests {
	const int Window = 128;

	static (SentencePieceOnnx.Pmm12EmbeddingGenerator Generator, Tokenizer Tokenizer) Model() {
		var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();
		return (generator, ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>());
	}

	// The shape JsonNormalizer emits, as Markdown paragraphs — the closest an IngestionDocument gets
	// to flattened JSON without inventing structure the payload does not have.
	static string FlattenedPayload() =>
		string.Join("\n\n", Enumerable.Range(0, 120)
			.Select(i => i == 117
				? "failure reason: the payment was declined because the card expired,"
				: $"campo {i}: Sérgio Silveira pagou o café número {i},"));

	[Test]
	public async ValueTask document_token_chunker_respects_the_window(CancellationToken cancellationToken) {
		// Arrange
		var (generator, tokenizer) = Model();
		using var _ = generator;

		var document = BuildDocument(FlattenedPayload());
		var chunker  = new DocumentTokenChunker(new IngestionChunkerOptions(tokenizer) {
			MaxTokensPerChunk = Window,
			OverlapTokens     = 0,
		});

		// Act
		var chunks = await Collect(chunker, document, cancellationToken);

		// Assert
		Report("DocumentTokenChunker", tokenizer, chunks);

		await Assert.That(chunks.Count).IsGreaterThan(1);
		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);
	}

	[Test]
	public async ValueTask semantic_similarity_chunker_respects_the_window(CancellationToken cancellationToken) {
		// Arrange
		var (generator, tokenizer) = Model();
		using var _ = generator;

		var document = BuildDocument(FlattenedPayload());
		var chunker  = new SemanticSimilarityChunker(
			generator,
			new IngestionChunkerOptions(tokenizer) { MaxTokensPerChunk = Window, OverlapTokens = 0 },
			thresholdPercentile: null);

		// Act — timed, because this one embeds to decide boundaries and the records path is
		// throughput-bound.
		var started = DateTimeOffset.UtcNow;
		var chunks  = await Collect(chunker, document, cancellationToken);
		var elapsed = DateTimeOffset.UtcNow - started;

		// Assert
		Report($"SemanticSimilarityChunker ({elapsed.TotalSeconds:F1}s)", tokenizer, chunks);

		await Assert.That(chunks.Count).IsGreaterThan(0);
		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);
	}

	// No file reader needed after all: IngestionDocument, IngestionDocumentSection and
	// IngestionDocumentParagraph are all constructible directly, so flattened JSON goes in as one
	// paragraph per "key: value" line without inventing Markdown structure the payload lacks.
	static IngestionDocument BuildDocument(string payload) {
		var section = new IngestionDocumentSection();

		foreach (var line in payload.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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

	static void Report(string label, Tokenizer tokenizer, IReadOnlyList<string> chunks) {
		var counts = chunks.Select(chunk => tokenizer.CountTokens(chunk)).ToArray();

		Console.WriteLine(new StringBuilder()
			.AppendLine()
			.AppendLine(label)
			.AppendLine($"  chunks : {chunks.Count}")
			.AppendLine($"  tokens : min {counts.Min()}  max {counts.Max()}  window {Window}")
			.AppendLine($"  over   : {counts.Count(count => count > Window)}")
			.ToString());
	}
}
