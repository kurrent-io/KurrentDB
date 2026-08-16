// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Kontext.Tests.Unit;

/// <summary>
/// Pins the config file contract: KontextOptions is the ONE owner of the KurrentDB:Kontext
/// section, the prototype's Provider + per-provider-block shape survives unchanged, and the
/// registry's keys live inside the Local block instead of competing for the section.
/// </summary>
public class KontextOptionsBindingTests {
	[Test]
	public async ValueTask defaults_hold_when_the_section_is_empty(CancellationToken cancellationToken) {
		// Arrange + Act
		var options = Bind([]);

		// Assert — zero config boots local 384-dim, disabled.
		await Assert.That(options.Enabled).IsFalse();
		await Assert.That(options.Embeddings.Provider).IsEqualTo(EmbeddingsProvider.Local);
		await Assert.That(options.Embeddings.Dimension).IsEqualTo(384);
		await Assert.That(options.Embeddings.BatchSize).IsEqualTo(1);
		await Assert.That(options.Embeddings.Local.ModelsDirectory).IsEqualTo("");
		await Assert.That(options.Embeddings.Local.Models.Count).IsEqualTo(0);
	}

	[Test]
	public async ValueTask binds_a_remote_provider_file(CancellationToken cancellationToken) {
		// Arrange + Act — the file shape users write: only the active provider's block appears.
		var options = Bind(new() {
			["KurrentDB:Kontext:Enabled"]                    = "true",
			["KurrentDB:Kontext:Embeddings:Provider"]        = "OpenAI",
			["KurrentDB:Kontext:Embeddings:Dimension"]       = "1536",
			["KurrentDB:Kontext:Embeddings:OpenAI:ApiKey"]   = "sk-test",
			["KurrentDB:Kontext:Embeddings:OpenAI:Model"]    = "text-embedding-3-small",
		});

		// Assert — including the BatchSize passthrough following the active provider.
		await Assert.That(options.Enabled).IsTrue();
		await Assert.That(options.Embeddings.Provider).IsEqualTo(EmbeddingsProvider.OpenAI);
		await Assert.That(options.Embeddings.Dimension).IsEqualTo(1536);
		await Assert.That(options.Embeddings.OpenAI.ApiKey).IsEqualTo("sk-test");
		await Assert.That(options.Embeddings.BatchSize).IsEqualTo(256);
	}

	[Test]
	public async ValueTask binds_the_registry_keys_inside_the_local_block(CancellationToken cancellationToken) {
		// Arrange + Act — the unification: ModelsDirectory and Models are Local's keys now,
		// not a second reader of the Embeddings section.
		var options = Bind(new() {
			["KurrentDB:Kontext:Embeddings:Provider"]                        = "Local",
			["KurrentDB:Kontext:Embeddings:Local:ModelsDirectory"]           = "/var/lib/kontext/models",
			["KurrentDB:Kontext:Embeddings:Local:ModelId"]                   = "multilingual-e5-small",
			["KurrentDB:Kontext:Embeddings:Local:Models:0:Key"]              = "multilingual-e5-small",
			["KurrentDB:Kontext:Embeddings:Local:Models:0:Model"]            = "model.onnx",
			["KurrentDB:Kontext:Embeddings:Local:Models:0:Assets:0"]         = "sentencepiece.bpe.model",
		});

		// Assert
		await Assert.That(options.Embeddings.Local.ModelsDirectory).IsEqualTo("/var/lib/kontext/models");
		await Assert.That(options.Embeddings.Local.ModelId).IsEqualTo("multilingual-e5-small");
		await Assert.That(options.Embeddings.Local.Models.Count).IsEqualTo(1);
		await Assert.That(options.Embeddings.Local.Models[0].Key).IsEqualTo("multilingual-e5-small");
		await Assert.That(options.Embeddings.Local.Models[0].Assets[0]).IsEqualTo("sentencepiece.bpe.model");
	}

	static KontextOptions Bind(Dictionary<string, string?> values) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(values)
			.Build()
			.GetSection("KurrentDB:Kontext")
			.Get<KontextOptions>() ?? new KontextOptions();
}
