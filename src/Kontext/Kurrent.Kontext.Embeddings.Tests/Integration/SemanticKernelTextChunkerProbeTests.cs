// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel.Text;

// TextChunker is [Experimental("SKEXP0050")] — "subject to change or removal in future updates" —
// so it cannot be called without suppressing the diagnostic. That is a fact about adopting it, not
// noise: Microsoft is moving this surface to Microsoft.Extensions.DataIngestion.
#pragma warning disable SKEXP0050

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// Whether Semantic Kernel's <c>TextChunker</c> can do the chunking instead of a hand-written one.
/// It takes a <c>TokenCounter</c> delegate, so the model's own tokenizer plugs in, and it claims the
/// separator ladder — "new lines first, then periods, and so on" — which is the part a hand-written
/// chunker gets wrong.
/// </summary>
/// <remarks>
/// The claim under test is not that it splits, but that it NEVER exceeds the window. A chunker that
/// leaves one oversized piece whole hands the model text it will silently truncate, which is the
/// failure this whole exercise exists to remove. The oversized cases below have no newline and no
/// punctuation, so they can only be handled by falling all the way down the ladder.
/// </remarks>
[Category("Integration")]
public class SemanticKernelTextChunkerProbeTests {
	const int Window = 128;   // pmm12's trained max_seq_length

	static Tokenizer Tokenizer() {
		var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();
		return ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();
	}

	[Test]
	public async ValueTask splits_normalized_json_lines_within_the_window() {
		// Arrange — the shape JsonNormalizer emits: one "key: value" per line.
		var tokenizer = Tokenizer();
		var lines = Enumerable.Range(0, 200)
			.Select(i => $"campo {i}: Sérgio Silveira pagou o café número {i},")
			.ToArray();

		// Act
		var chunks = TextChunker.SplitPlainTextParagraphs(lines, Window, overlapTokens: 0,
			chunkHeader: null, tokenCounter: text => tokenizer.CountTokens(text));

		// Assert
		await Assert.That(chunks.Count).IsGreaterThan(1);

		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);

		Report("normalized json lines", tokenizer, chunks);
	}

	[Test]
	public async ValueTask splits_a_single_line_that_is_longer_than_the_window() {
		// Arrange — one line, no newline to split on, but sentence punctuation the ladder can use.
		var tokenizer = Tokenizer();
		var oneLongLine = string.Join(' ', Enumerable.Range(0, 120)
			.Select(i => $"O Sérgio pagou o café número {i}."));

		// Act
		var chunks = TextChunker.SplitPlainTextParagraphs([oneLongLine], Window, overlapTokens: 0,
			chunkHeader: null, tokenCounter: text => tokenizer.CountTokens(text));

		// Assert — the whole question: does it descend past newlines to punctuation?
		await Assert.That(tokenizer.CountTokens(oneLongLine)).IsGreaterThan(Window);

		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);

		Report("one long line, with punctuation", tokenizer, chunks);
	}

	[Test]
	public async ValueTask splits_a_single_line_with_no_punctuation_at_all() {
		// Arrange — the worst case, and the one a records payload actually produces: a long value
		// with no newline, no period, nothing but spaces. Only the bottom of the ladder can cut it.
		var tokenizer = Tokenizer();
		var noPunctuation = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"valor{i}"));

		// Act
		var chunks = TextChunker.SplitPlainTextParagraphs([noPunctuation], Window, overlapTokens: 0,
			chunkHeader: null, tokenCounter: text => tokenizer.CountTokens(text));

		// Assert
		await Assert.That(tokenizer.CountTokens(noPunctuation)).IsGreaterThan(Window);

		var oversized = chunks.Where(chunk => tokenizer.CountTokens(chunk) > Window).ToArray();

		Report("one long line, NO punctuation", tokenizer, chunks);

		if (oversized.Length > 0)
			Console.WriteLine($"OVERSIZED CHUNKS: {oversized.Length} of {chunks.Count} exceed the window "
			                + $"— largest {oversized.Max(chunk => tokenizer.CountTokens(chunk))} tokens. "
			                + "The ladder does not reach a hard split; we still need one.");

		await Assert.That(oversized.Length).IsEqualTo(0);
	}

	static void Report(string label, Tokenizer tokenizer, IReadOnlyList<string> chunks) {
		var counts = chunks.Select(chunk => tokenizer.CountTokens(chunk)).ToArray();

		Console.WriteLine(new StringBuilder()
			.AppendLine()
			.AppendLine($"{label}")
			.AppendLine($"  chunks : {chunks.Count}")
			.AppendLine($"  tokens : min {counts.Min()}  max {counts.Max()}  window {Window}")
			.ToString());
	}
}
