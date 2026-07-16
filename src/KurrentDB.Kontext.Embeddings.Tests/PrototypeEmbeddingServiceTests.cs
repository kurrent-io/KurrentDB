// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Embeddings.Tests;

/// <summary>
/// A = the default local implementation (<see cref="EmbeddingService"/>, moved verbatim, byte-faithful
/// to vectors already stored in existing indexes). It runs all-MiniLM via a hand-rolled WordPiece
/// tokenizer that does NOT fold accents — that weakness is precisely why it stays frozen as the baseline.
/// </summary>
public class PrototypeEmbeddingServiceTests {
	static EmbeddingService _service = null!;

	[Before(Class)]
	public static async Task Setup(ClassHookContext context) {
		_service = new EmbeddingService(new ModelManager(NullLogger<ModelManager>.Instance), NullLogger<EmbeddingService>.Instance);
		await _service.InitializeAsync();
	}

	[After(Class)]
	public static Task Teardown(ClassHookContext context) {
		_service.Dispose();
		return Task.CompletedTask;
	}

	static async Task<float[]> Embed(string text) => (await _service.GenerateAsync([text]))[0].Vector.ToArray();

	[Test]
	public async ValueTask produces_384_dimensional_l2_normalized_vectors() {
		// Act
		var vector = await Embed("event-native database");

		// Assert
		await Assert.That(vector.Length).IsEqualTo(EmbeddingsTestSupport.Dimensions);
		await Assert.That(EmbeddingsTestSupport.L2Norm(vector)).IsEqualTo(1.0).Within(1e-3);
	}

	[Test]
	public async ValueTask identical_text_embeds_identically() {
		// Act
		var first = await Embed("hello world");
		var second = await Embed("hello world");

		// Assert
		await Assert.That(EmbeddingsTestSupport.Cosine(first, second)).IsEqualTo(1.0).Within(1e-6);
	}

	[Test]
	public async ValueTask does_not_fold_accents_the_frozen_baseline_weakness() {
		// The hand-rolled tokenizer never strips accents, so "café" and "cafe" diverge badly. Pinning this
		// low ceiling guards the baseline: if it ever started folding accents, stored vectors would shift.
		const double AccentBlindCeiling = 0.7;

		// Act
		var accented = await Embed("café");
		var plain = await Embed("cafe");

		// Assert
		await Assert.That(EmbeddingsTestSupport.Cosine(accented, plain)).IsLessThan(AccentBlindCeiling);
	}
}
