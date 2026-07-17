// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// B = all-MiniLM (the SAME model as A) + Microsoft.ML.Tokenizers BertTokenizer. Its whole reason to
/// exist is the accent fix: it always lower-cases and strips accents, so accented and plain forms embed
/// identically. On plain ASCII it reproduces A (same ONNX, equivalent tokenization).
/// </summary>
public class WordPieceOnnxEmbeddingGeneratorTests {
	static IEmbeddingGenerator<string, Embedding<float>> _generator = null!;
	static EmbeddingService _prototype = null!;

	[Before(Class)]
	public static async Task Setup(ClassHookContext context) {
		// B reads the embedded all-MiniLM directly (the avx2 int8 variant — ModelManager loads the same one
		// on ARM), so it stays the same generator as A on ASCII while being decoupled from the prototype
		// loader. A (the prototype) still needs ModelManager for the reproduces-A comparison below.
		var bModel = OnnxModel.FromEmbeddedResources(
			"all-MiniLM-L6-v2",
			typeof(KontextModelsAssembly).Assembly,
			"KurrentDB.Kontext.Models.embedding.model_quint8_avx2.onnx",
			new Dictionary<string, string> { ["vocab.txt"] = "KurrentDB.Kontext.Models.embedding.vocab.txt" });
		_generator = new WordPieceOnnxEmbeddingGenerator(bModel);

		var models = new ModelManager(NullLogger<ModelManager>.Instance);
		_prototype = new EmbeddingService(models, NullLogger<EmbeddingService>.Instance);
		await _prototype.InitializeAsync();
	}

	[After(Class)]
	public static Task Teardown(ClassHookContext context) {
		_generator.Dispose();
		_prototype.Dispose();
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
	public async ValueTask folds_accents_so_accented_and_plain_forms_match() {
		// The fix: "café" and "cafe" must land on (essentially) the same vector.
		const double AccentRobustFloor = 0.99;

		// Act
		var accented = await Embed("café");
		var plain = await Embed("cafe");

		// Assert
		await Assert.That(EmbeddingsTestSupport.Cosine(accented, plain)).IsGreaterThan(AccentRobustFloor);
	}

	[Test]
	public async ValueTask reproduces_the_prototype_on_plain_ascii() {
		// Same ONNX + equivalent WordPiece tokenization => the same generator as A on ASCII input.
		const double SameGeneratorFloor = 0.9999;
		const string Text = "hello world";

		// Act
		var fromBert = await Embed(Text);
		var fromPrototype = (await _prototype.GenerateAsync([Text]))[0].Vector.ToArray();

		// Assert
		await Assert.That(EmbeddingsTestSupport.Cosine(fromBert, fromPrototype)).IsGreaterThan(SameGeneratorFloor);
	}
}
