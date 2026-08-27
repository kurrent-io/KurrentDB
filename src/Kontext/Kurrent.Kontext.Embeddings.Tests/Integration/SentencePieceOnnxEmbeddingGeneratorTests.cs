// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// C = multilingual-e5-small (XLM-R SentencePiece + fairseq remap), the multilingual showcase. Its
/// distinguishing capability is CROSS-LINGUAL alignment — the same concept across languages/scripts
/// embeds close together — which neither A nor B can do.
/// </summary>
[Category("Integration")]
public class SentencePieceOnnxEmbeddingGeneratorTests {
	static EmbeddingGenerator _generator = null!;

	[Before(Class)]
	public static Task Setup(ClassHookContext context) {
		EmbeddingsTestSupport.EnsureE5();

		// e5 requires the "query: " prefix (matches how the reference vectors were produced).
		_generator = new SentencePieceOnnxEmbeddingGenerator(
			OnnxModel.FromFiles("multilingual-e5-small", EmbeddingsTestSupport.E5ModelPath,
			new Dictionary<string, string> { ["sentencepiece.bpe.model"] = EmbeddingsTestSupport.E5SentencePiecePath }),
			new SentencePieceOnnxOptions { InputPrefix = "query: " });

		return Task.CompletedTask;
	}

	[After(Class)]
	public static Task Teardown(ClassHookContext context) {
		_generator?.Dispose();
		return Task.CompletedTask;
	}

	static async Task<float[]> Embed(string text) => (await _generator.GenerateAsync([text]))[0].Vector.ToArray();

	[Test]
	public async ValueTask produces_384_dimensional_l2_normalized_vectors() {
		// Act
		var vector = await Embed("event-native database");

		// Assert
		await Assert.That(vector.Length).IsEqualTo(EmbeddingsTestSupport.Dimensions);
		await Assert.That(EmbeddingsTestSupport.L2Norm(vector)).IsEqualTo(1.0).Within(1e-3);
	}

	[Test]
	public async ValueTask aligns_the_same_concept_across_languages() {
		// The whole point of C: Cyrillic "Москва" and English "Moscow" are the same concept -> close vectors.
		const double CrossLingualFloor = 0.9;

		// Act
		var cyrillic = await Embed("Москва");
		var english = await Embed("Moscow");

		// Assert
		await Assert.That(EmbeddingsTestSupport.Cosine(cyrillic, english)).IsGreaterThan(CrossLingualFloor);
	}

	[Test]
	public async ValueTask folds_accents() {
		const double AccentRobustFloor = 0.95;

		// Act
		var accented = await Embed("café");
		var plain = await Embed("cafe");

		// Assert
		await Assert.That(EmbeddingsTestSupport.Cosine(accented, plain)).IsGreaterThan(AccentRobustFloor);
	}

	[Test]
	public async ValueTask embeds_non_latin_script_without_collapsing() {
		// Japanese must yield a well-formed unit vector, unlike the English-only A/B paths.
		// Act
		var vector = await Embed("日本語");

		// Assert
		await Assert.That(vector.Length).IsEqualTo(EmbeddingsTestSupport.Dimensions);
		await Assert.That(EmbeddingsTestSupport.L2Norm(vector)).IsEqualTo(1.0).Within(1e-3);
	}
}
