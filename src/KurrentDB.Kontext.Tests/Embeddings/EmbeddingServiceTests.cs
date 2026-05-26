// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class EmbeddingServiceTests {
	static EmbeddingService? _service;

	[Before(Class)]
	public static async Task Setup(ClassHookContext context) {
		var modelManager = new ModelManager(NullLogger<ModelManager>.Instance);
		_service = new EmbeddingService(modelManager,
			NullLogger<EmbeddingService>.Instance);
		await _service.InitializeAsync();
	}

	[After(Class)]
	public static async Task Teardown(ClassHookContext context) {
		_service?.Dispose();
		await Task.CompletedTask;
	}

	[Test]
	public async Task GetEmbedding_Returns_Correct_Dimensions() {
		var embedding = _service!.GetEmbedding("hello world");
		await Assert.That(embedding.Length).IsEqualTo(EmbeddingService.Dimensions);
	}

	[Test]
	public async Task GetEmbedding_Is_Normalized() {
		var embedding = _service!.GetEmbedding("order placed for a widget");

		// L2 norm should be approximately 1.0
		var norm = MathF.Sqrt(embedding.Sum(x => x * x));
		await Assert.That(norm).IsBetween(0.99f, 1.01f);
	}

	[Test]
	public async Task GetEmbedding_Similar_Texts_Have_High_Similarity() {
		var emb1 = _service!.GetEmbedding("order placed");
		var emb2 = _service!.GetEmbedding("order was placed");

		var similarity = DotProduct(emb1, emb2);
		// Similar texts should have cosine similarity > 0.8
		await Assert.That(similarity).IsGreaterThan(0.8f);
	}

	[Test]
	public async Task GetEmbedding_Different_Texts_Have_Lower_Similarity() {
		var emb1 = _service!.GetEmbedding("order placed for widgets");
		var emb2 = _service!.GetEmbedding("the weather is sunny today");

		var similarity = DotProduct(emb1, emb2);
		// Unrelated texts should have lower similarity
		await Assert.That(similarity).IsLessThan(0.5f);
	}

	[Test]
	public async Task GetEmbedding_Deterministic() {
		var emb1 = _service!.GetEmbedding("hello world");
		var emb2 = _service!.GetEmbedding("hello world");

		for (var i = 0; i < emb1.Length; i++)
			await Assert.That(emb1[i]).IsEqualTo(emb2[i]);
	}

	[Test]
	public async Task GetEmbedding_Empty_String_Does_Not_Throw() {
		var embedding = _service!.GetEmbedding("");
		await Assert.That(embedding.Length).IsEqualTo(EmbeddingService.Dimensions);
	}

	[Test]
	public async Task GetEmbedding_Long_Text_Does_Not_Throw() {
		var longText = string.Join(" ", Enumerable.Repeat("word", 500));
		var embedding = _service!.GetEmbedding(longText);
		await Assert.That(embedding.Length).IsEqualTo(EmbeddingService.Dimensions);
	}

	[Test]
	public async Task GetEmbedding_Not_Initialized_Throws() {
		var modelManager = new ModelManager(NullLogger<ModelManager>.Instance);
		var uninitService = new EmbeddingService(modelManager,
			NullLogger<EmbeddingService>.Instance);

		await Assert.That(() => uninitService.GetEmbedding("test"))
			.Throws<InvalidOperationException>();
	}

	static float DotProduct(float[] a, float[] b) {
		float sum = 0;
		for (var i = 0; i < a.Length; i++)
			sum += a[i] * b[i];
		return sum;
	}
}