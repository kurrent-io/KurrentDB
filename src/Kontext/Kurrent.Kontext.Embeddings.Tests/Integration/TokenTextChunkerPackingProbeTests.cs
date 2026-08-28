// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.Chunking;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using SemanticKernelChunker = Microsoft.SemanticKernel.Text.TextChunker;

#pragma warning disable SKEXP0050

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// What pack-then-bound actually costs against Semantic Kernel's documented bound-then-pack, using
/// the shipped chunker rather than a sketch of it.
/// </summary>
/// <remarks>
/// The ordering is the only thing TokenTextChunker contributes over calling the library directly, and
/// it is justified by chunk count alone: a chunk is a vector, and MaxSim compares the query against
/// every vector in the row on every search. If the counts come out level, the ordering is not worth
/// keeping and the chunker should call the two-step and stop there.
/// </remarks>
[Category("Integration")]
[Timeout(120_000)]
public class TokenTextChunkerPackingProbeTests {
	const int Window = 128;

	[Test]
	public async ValueTask reports_what_the_ordering_buys_over_the_two_step_call() {
		// Arrange
		using var generator = new Pmm12EmbeddingGenerator();
		var tokenizer = ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();

		Console.WriteLine($"\n{"shape",-32} {"shipped",8} {"two-step",9} {"saved",7}");

		var savings = new List<double>();

		// Act
		foreach (var (label, text, header) in Cases()) {
			var chunker = new TokenTextChunker(generator,
				new TextChunkerOptions { MaxTokens = Window, ChunkHeader = header });

			var shipped = chunker.Chunk(text);

			var lines   = SemanticKernelChunker.SplitPlainTextLines(text, Window, t => tokenizer.CountTokens(t));
			var twoStep = SemanticKernelChunker.SplitPlainTextParagraphs(
				lines, Window, 0, header, t => tokenizer.CountTokens(t));

			var saved = 1.0 - (double)shipped.Count / twoStep.Count;
			savings.Add(saved);

			Console.WriteLine($"{label,-32} {shipped.Count,8} {twoStep.Count,9} {saved,7:P0}");

			// Assert — whatever the packing, the invariant holds. This is the part that is not a
			// preference: an oversized chunk is silently truncated by the model.
			foreach (var chunk in shipped)
				await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);
		}

		Console.WriteLine($"\nmean saving: {savings.Average():P0}");
	}

	static (string Label, string Text, string? Header)[] Cases() => [
		("memory prose", Prose(), null),
		("memory prose + title", Prose(), "Sérgio's café preference\n"),
		("record, flattened json", FlattenedJson(), null),
		("record, one long line", string.Join(' ', Enumerable.Range(0, 400).Select(i => $"valor{i}")), null),
	];

	static string Prose() => string.Join('\n', Enumerable.Range(0, 60).SelectMany(i => new[] {
		$"Sérgio pagou o café número {i} e comentou que a reunião correu bem.",
		$"Ele mencionou que o pagamento {i} foi recusado uma vez antes de ser aceite.",
	}));

	static string FlattenedJson() => string.Join('\n', Enumerable.Range(0, 120)
		.Select(i => $"campo {i}: Sérgio Silveira pagou o café número {i},"));
}
