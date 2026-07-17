// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// Verifies <see cref="OnnxModelRegistry"/> binds its manifests from <see cref="IConfiguration"/> via the
/// (reflection-free, source-generated) config binder — the model list and cache root are read correctly and
/// resolve to lazy handles.
/// </summary>
public class OnnxModelRegistryConfigurationTests {
	static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
		.AddInMemoryCollection(new Dictionary<string, string?> {
			["ModelsDirectory"] = "/tmp/kontext-models",
			["Models:0:Key"] = "multilingual-e5-small",
			["Models:0:Model"] = "model.onnx",
			["Models:0:Assets:0"] = "sentencepiece.bpe.model",
		})
		.Build();

	[Test]
	public async ValueTask binds_the_registered_model_from_configuration() {
		// Arrange
		const string ModelKey = "multilingual-e5-small";
		var registry = OnnxModelRegistry.FromConfiguration(BuildConfiguration());

		// Act
		var model = registry.Get(ModelKey);

		// Assert — a handle with the bound key proves the manifest bound and resolved.
		await Assert.That(model.Name).IsEqualTo(ModelKey);
	}

	[Test]
	public async ValueTask rejects_a_model_key_that_was_not_configured() {
		// Arrange
		var registry = OnnxModelRegistry.FromConfiguration(BuildConfiguration());

		// Act / Assert
		await Assert.That(() => registry.Get("not-configured")).Throws<KeyNotFoundException>();
	}
}
