// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;

namespace KurrentDB.Kontext.Embeddings.Tests;

/// <summary>
/// Exercises the interim embedded-resource bridge: <see cref="OnnxModel.FromEmbeddedResources"/> reading the
/// pMM12 (paraphrase-multilingual-MiniLM-L12-v2) model + shared XLM-R tokenizer that the
/// <c>KurrentDB.Kontext.Models</c> build target embeds, driven through the SentencePiece generator (C). Unlike
/// the e5 C tests, no Playground download is needed — pMM12 rides inside the models assembly after any build.
/// </summary>
public class OnnxModelFromEmbeddedResourcesTests {
	[Test]
	public async ValueTask reads_pmm12_from_embedded_resources_and_produces_a_unit_vector() {
		// Arrange
		var model = OnnxModel.FromEmbeddedResources(
			"paraphrase-multilingual-MiniLM-L12-v2",
			typeof(KontextModelsAssembly).Assembly,
			"KurrentDB.Kontext.Models.pmm12.model.onnx",
			new Dictionary<string, string> { ["sentencepiece.bpe.model"] = "KurrentDB.Kontext.Models.pmm12.sentencepiece.bpe.model" });
		using var generator = new SentencePieceOnnxEmbeddingGenerator(model, new SentencePieceOnnxOptions { InputPrefix = null });

		// Act
		var vector = (await generator.GenerateAsync(["event-native database"]))[0].Vector.ToArray();

		// Assert
		await Assert.That(vector.Length).IsEqualTo(EmbeddingsTestSupport.Dimensions);
		await Assert.That(EmbeddingsTestSupport.L2Norm(vector)).IsEqualTo(1.0).Within(1e-3);
	}
}
