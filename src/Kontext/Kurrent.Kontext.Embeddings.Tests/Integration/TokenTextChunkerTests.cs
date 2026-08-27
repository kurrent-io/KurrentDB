// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.Chunking;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// The chunker against the properties the storage layer depends on: no chunk exceeds the model's
/// window, short text stays one chunk, and a chunk header rides inside every chunk.
/// </summary>
[Category("Integration")]
[Timeout(120_000)]
public class TokenTextChunkerTests {
	const int Window = 128;

	static (Pmm12EmbeddingGenerator Generator, Tokenizer Tokenizer) Model() {
		var generator = new Pmm12EmbeddingGenerator();
		return (generator, ((IEmbeddingGenerator)generator).GetRequiredService<Tokenizer>());
	}

	[Test]
	public async ValueTask reads_the_window_from_the_generator_when_it_is_not_configured() {
		// Arrange
		var (generator, tokenizer) = Model();
		using var _ = generator;

		var chunker = new TokenTextChunker(generator, new TextChunkerOptions());   // MaxTokens unset

		// Act
		var chunks = chunker.Chunk(LongProse());

		// Assert — pmm12's trained window is 128, so an unconfigured chunker must land there and not
		// on SentencePieceOnnxOptions' generic 512 default.
		await Assert.That(chunks.Count).IsGreaterThan(1);
		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);
	}

	[Test]
	public async ValueTask keeps_short_text_as_a_single_chunk() {
		// Arrange
		var (generator, _) = Model();
		using var __ = generator;

		var chunker = new TokenTextChunker(generator, new TextChunkerOptions { MaxTokens = Window });
		const string Memory = "Sérgio prefers the morning sessions because the café is quieter then.";

		// Act
		var chunks = chunker.Chunk(Memory);

		// Assert — the common case takes the same path as the long one and comes back untouched.
		await Assert.That(chunks.Count).IsEqualTo(1);
		await Assert.That(chunks[0]).IsEqualTo(Memory);
	}

	[Test]
	public async ValueTask never_returns_empty_for_blank_input() {
		// Arrange
		var (generator, _) = Model();
		using var __ = generator;

		var chunker = new TokenTextChunker(generator, new TextChunkerOptions { MaxTokens = Window });

		// Act + Assert — a caller zipping chunks back to records must never receive nothing.
		await Assert.That(chunker.Chunk("").Count).IsEqualTo(1);
		await Assert.That(chunker.Chunk("   ").Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask puts_the_header_in_every_chunk_without_overflowing_the_window() {
		// Arrange
		var (generator, tokenizer) = Model();
		using var _ = generator;

		const string Title = "Sérgio's café preference";

		var chunker = new TokenTextChunker(generator, new TextChunkerOptions {
			MaxTokens   = Window,
			ChunkHeader = $"{Title}\n",
		});

		// Act
		var chunks = chunker.Chunk(LongProse());

		// Assert — the point of the header: a chunk that lost its subject still names it.
		await Assert.That(chunks.Count).IsGreaterThan(1);

		foreach (var chunk in chunks)
			await Assert.That(chunk).Contains(Title);

		// And the budget question — the header costs tokens out of each chunk, so the window has to
		// account for it rather than being blown by it.
		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);
	}

	[Test]
	public async ValueTask splits_a_line_that_has_no_punctuation_and_exceeds_the_window() {
		// Arrange — the shape a records payload produces: a long value with no sentence structure,
		// where only the bottom of the separator ladder can cut.
		var (generator, tokenizer) = Model();
		using var _ = generator;

		var chunker = new TokenTextChunker(generator, new TextChunkerOptions { MaxTokens = Window });
		var oneLine = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"valor{i}"));

		// Act
		var chunks = chunker.Chunk(oneLine);

		// Assert
		await Assert.That(tokenizer.CountTokens(oneLine)).IsGreaterThan(Window);

		foreach (var chunk in chunks)
			await Assert.That(tokenizer.CountTokens(chunk)).IsLessThanOrEqualTo(Window);
	}

	static string LongProse() => string.Join('\n', Enumerable.Range(0, 60).SelectMany(i => new[] {
		$"Sérgio pagou o café número {i} e comentou que a reunião correu bem.",
		$"Ele mencionou que o pagamento {i} foi recusado uma vez antes de ser aceite.",
	}));
}
