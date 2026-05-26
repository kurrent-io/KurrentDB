// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class ModelManagerTests {
	[Test]
	public async Task EmbeddingModel_Loads_NonEmpty_Bytes() {
		var manager = new ModelManager(NullLogger<ModelManager>.Instance);
		await Assert.That(manager.EmbeddingModel.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task EmbeddingVocab_Loads_NonEmpty_Bytes() {
		var manager = new ModelManager(NullLogger<ModelManager>.Instance);
		await Assert.That(manager.EmbeddingVocab.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task CrossEncoderModel_Loads_NonEmpty_Bytes() {
		var manager = new ModelManager(NullLogger<ModelManager>.Instance);
		await Assert.That(manager.CrossEncoderModel.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task CrossEncoderVocab_Loads_NonEmpty_Bytes() {
		var manager = new ModelManager(NullLogger<ModelManager>.Instance);
		await Assert.That(manager.CrossEncoderVocab.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task Model_And_Vocab_Properties_Are_Cached() {
		var manager = new ModelManager(NullLogger<ModelManager>.Instance);
		await Assert.That(manager.EmbeddingModel).IsSameReferenceAs(manager.EmbeddingModel);
		await Assert.That(manager.EmbeddingVocab).IsSameReferenceAs(manager.EmbeddingVocab);
		await Assert.That(manager.CrossEncoderModel).IsSameReferenceAs(manager.CrossEncoderModel);
		await Assert.That(manager.CrossEncoderVocab).IsSameReferenceAs(manager.CrossEncoderVocab);
	}
}