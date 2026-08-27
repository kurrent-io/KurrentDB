// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// The pieces a chunker needs, taken from the framework rather than hand-written: reaching the
/// model's own tokenizer through <c>IEmbeddingGenerator.GetService</c>, and cutting text with
/// <c>Tokenizer.GetIndexByTokenCount</c>.
/// </summary>
/// <remarks>
/// Both are documented; neither is exercised anywhere in this repo, and the docs describe
/// Microsoft.ML.Tokenizers 2.0.0-preview while we pin 2.0.0. This pins the behaviour we would build
/// on — that the retrieved tokenizer is the SAME instance the generator embeds with, and that the
/// index it returns really does keep every piece inside the window.
/// </remarks>
[Category("Integration")]
public class TokenizerServiceProbeTests {
	[Test]
	public async ValueTask the_generator_hands_out_the_tokenizer_it_embeds_with() {
		// Arrange
		using var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();

		// Act — the framework's service-retrieval pattern, not a bespoke accessor.
		var tokenizer = ((IEmbeddingGenerator)generator).GetService<Tokenizer>();

		// Assert
		await Assert.That(tokenizer).IsNotNull();
		await Assert.That(tokenizer).IsTypeOf<SentencePieceTokenizer>();

		// Asking twice returns the same object: one tokenizer, not a copy per call.
		await Assert.That(((IEmbeddingGenerator)generator).GetService<Tokenizer>())
			.IsSameReferenceAs(tokenizer);
	}

	[Test]
	public async ValueTask count_tokens_and_get_index_by_token_count_behave_as_documented() {
		// Arrange
		using var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();
		var tokenizer = ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();

		const int Window = 128;   // pmm12's trained max_seq_length

		var text = string.Join(' ', Enumerable.Range(0, 400)
			.Select(i => i % 7 == 0 ? "Sérgio pagou o café" : $"campo {i} valor rotineiro"));

		// Act
		var total = tokenizer.CountTokens(text);
		var cut   = tokenizer.GetIndexByTokenCount(text, Window, out _, out var cutTokens);

		// Assert — the text is genuinely past the window, so the cut is meaningful.
		await Assert.That(total).IsGreaterThan(Window);

		// The contract: the index is where the last INCLUDED character ends, and the token count of
		// everything before it never exceeds the limit.
		await Assert.That(cutTokens).IsLessThanOrEqualTo(Window);
		await Assert.That(tokenizer.CountTokens(text[..cut])).IsLessThanOrEqualTo(Window);

		// And it is not trivially small — it should fill the window, not stop at the first word.
		await Assert.That(cutTokens).IsGreaterThan(Window / 2);
	}

	[Test]
	public async ValueTask chunking_by_that_index_covers_the_whole_text_and_every_chunk_fits() {
		// Arrange
		using var generator = new SentencePieceOnnx.Pmm12EmbeddingGenerator();
		var tokenizer = ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>();

		const int Window = 128;

		// Accented and CJK text on purpose: a characters-per-token approximation is wrong here, which
		// is the reason to use the tokenizer rather than a heuristic.
		var text = string.Join(' ', Enumerable.Range(0, 300)
			.Select(i => i % 5 == 0 ? "日本語のテキスト" : $"Sérgio Silveira paguei o café número {i}"));

		// Act — accumulate whole lines while they fit, and close the chunk when the next one would
		// not. GetIndexByTokenCount is deliberately NOT used: its index is relative to the tokenizer's
		// normalized form, and SentencePiece normalization rewrites every space to U+2581 and prepends
		// one, so that index does not address the text we store. It is a truncation primitive, not a
		// chunking one. CountTokens asks a question about a string without handing back an offset into
		// a different string, which is what makes it safe here.
		List<string> chunks = [];
		var current = new StringBuilder();

		foreach (var line in text.Split(' ')) {
			if (current.Length > 0 && tokenizer.CountTokens($"{current} {line}") > Window) {
				chunks.Add(current.ToString());
				current.Clear();
			}

			current.Append(current.Length > 0 ? " " : "").Append(line);
		}

		if (current.Length > 0)
			chunks.Add(current.ToString());

		// Assert
		await Assert.That(chunks.Count).IsGreaterThan(1);

		// Nothing is lost and nothing is rewritten: the chunks rejoin into the ORIGINAL text. This is
		// the property the normalized-index approach cannot give, and the reason the content column
		// can still hold what the caller wrote.
		await Assert.That(string.Join(' ', chunks)).IsEqualTo(text);

		// Every chunk fits the window — the property the whole design rests on.
		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);

		var report = new StringBuilder()
			.AppendLine()
			.AppendLine($"text        : {text.Length} chars, {tokenizer.CountTokens(text)} tokens")
			.AppendLine($"window      : {Window}")
			.AppendLine($"chunks      : {chunks.Count}")
			.AppendLine($"tokens/chunk: {string.Join(", ", chunks.Select(chunk => tokenizer.CountTokens(chunk)))}");

		Console.WriteLine(report.ToString());
	}
}
