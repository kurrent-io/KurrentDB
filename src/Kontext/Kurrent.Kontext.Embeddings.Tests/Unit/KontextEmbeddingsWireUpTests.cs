// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings.Tests.Unit;

public class KontextEmbeddingsWireUpTests {
	[Test]
	public async ValueTask local_without_registry_config_registers_the_interim_generator(CancellationToken cancellationToken) {
		// Arrange
		var services = new ServiceCollection();

		// Act — zero-config Local: no models directory, no manifests.
		services.AddKontextEmbeddings(EmbeddingsProvider.Local);

		// Assert — a generator is registered and no registry was minted for it.
		await Assert.That(HasGenerator(services)).IsTrue();
		await Assert.That(services.Any(d => d.ServiceType == typeof(OnnxModelRegistry))).IsFalse();
	}

	[Test]
	public async ValueTask local_with_a_models_directory_registers_the_registry_generator(CancellationToken cancellationToken) {
		// Arrange
		var services = new ServiceCollection();
		var local = new LocalEmbeddingsOptions {
			ModelsDirectory = "/var/lib/kontext/models",
			ModelId         = "multilingual-e5-small",
			Models          = [new() { Key = "multilingual-e5-small", Model = "model.onnx", Assets = ["sentencepiece.bpe.model"] }],
		};

		// Act
		services.AddKontextEmbeddings(EmbeddingsProvider.Local, local);

		// Assert — the registry and the generator both land.
		await Assert.That(HasGenerator(services)).IsTrue();
		await Assert.That(services.Any(d => d.ServiceType == typeof(OnnxModelRegistry))).IsTrue();
	}

	[Test]
	public async ValueTask remote_provider_registers_its_generator(CancellationToken cancellationToken) {
		// Arrange
		var services = new ServiceCollection();

		// Act
		services.AddKontextEmbeddings(EmbeddingsProvider.Ollama);

		// Assert
		await Assert.That(HasGenerator(services)).IsTrue();
	}

	[Test]
	public async ValueTask dimension_probe_accepts_a_matching_generator(CancellationToken cancellationToken) {
		// Arrange
		var generator = new FixedDimensionGenerator(4);

		// Act + Assert — same dimension: no throw.
		await generator.EnsureDimensionAsync(4, cancellationToken);
	}

	[Test]
	public async ValueTask dimension_probe_rejects_a_mismatched_generator(CancellationToken cancellationToken) {
		// Arrange
		var generator = new FixedDimensionGenerator(4);

		// Act
		InvalidOperationException? refusal = null;
		try {
			await generator.EnsureDimensionAsync(384, cancellationToken);
		} catch (InvalidOperationException ex) {
			refusal = ex;
		}

		// Assert — mismatch is a startup failure, never a poisoned vector store.
		await Assert.That(refusal).IsNotNull();
	}

	static bool HasGenerator(ServiceCollection services) =>
		services.Any(d => d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>));

	sealed class FixedDimensionGenerator(int dimension) : IEmbeddingGenerator<string, Embedding<float>> {
		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
			values.Select(_ => new Embedding<float>(new float[dimension])).ToList()));

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}
}
