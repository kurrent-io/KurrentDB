// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// C = multilingual-e5-small (XLM-R SentencePiece + fairseq remap), the multilingual showcase. Uses the
/// e5 model the Embeddings Playground already downloaded. Its distinguishing capability is CROSS-LINGUAL
/// alignment — the same concept across languages/scripts embeds close together — which neither A nor B can do.
/// </summary>
[Category("Integration")]
public class SentencePieceOnnxEmbeddingGeneratorTests {
	static IEmbeddingGenerator<string, Embedding<float>> _generator = null!;

	[Before(Class)]
	public static Task Setup(ClassHookContext context) {
		if (!File.Exists(EmbeddingsTestSupport.E5ModelPath))
			throw new FileNotFoundException(
				$"e5 model not found at {EmbeddingsTestSupport.E5ModelPath}. Run the Embeddings Playground " +
				"once to download it into its models/ cache — these tests point at that copy.", EmbeddingsTestSupport.E5ModelPath);

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
