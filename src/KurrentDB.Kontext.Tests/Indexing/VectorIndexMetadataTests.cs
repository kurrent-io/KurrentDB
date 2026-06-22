// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;

namespace KurrentDB.Kontext.Tests.Indexing;

/// <summary>
/// Tests for the startup embedding probe: the vector index metadata is pinned from an
/// actual generated embedding, so the dimensions are always ground truth and a broken
/// provider configuration fails at startup rather than during indexing.
/// </summary>
public class VectorIndexMetadataTests {
	[Test]
	public async Task Probe_Pins_Provider_Model_And_Dimensions_From_The_Generated_Vector() {
		var generator = new ProbeGenerator("test-model", dimensions: 7);

		var meta = await VectorIndexMetadata.ProbeAsync(generator, "Test", CancellationToken.None);

		await Assert.That(meta.Provider).IsEqualTo("Test");
		await Assert.That(meta.Model).IsEqualTo("test-model");
		await Assert.That(meta.Dimensions).IsEqualTo(7);
		await Assert.That(generator.CallCount).IsEqualTo(1);
	}

	[Test]
	public async Task Probe_Records_A_Null_Model_As_The_Providers_Default() {
		// some providers' generators don't expose the model id; the provider name still
		// disambiguates the index.
		var generator = new ProbeGenerator(modelId: null, dimensions: 7);

		var meta = await VectorIndexMetadata.ProbeAsync(generator, "Test", CancellationToken.None);

		await Assert.That(meta.Provider).IsEqualTo("Test");
		await Assert.That(meta.Model).IsNull();
		await Assert.That(meta.Dimensions).IsEqualTo(7);
	}

	[Test]
	public async Task Probe_Wraps_Generation_Failures_With_A_Config_Pointing_Error() {
		var generator = new ProbeGenerator("test-model", dimensions: 7) {
			Failure = new HttpRequestException("401 unauthorized"),
		};

		await Assert.That(async () => { await VectorIndexMetadata.ProbeAsync(generator, "Test", CancellationToken.None); })
			.Throws<InvalidOperationException>()
			.WithMessageContaining("test-model");
	}

	[Test]
	public async Task Probe_Throws_On_An_Empty_Vector() {
		var generator = new ProbeGenerator("test-model", dimensions: 0);

		await Assert.That(async () => { await VectorIndexMetadata.ProbeAsync(generator, "Test", CancellationToken.None); })
			.Throws<InvalidOperationException>()
			.WithMessageContaining("empty");
	}

	// -------- LoadOrProbe: the probe result is cached so restart loops don't re-probe --------

	static string TempCachePath() =>
		Path.Combine(Path.GetTempPath(), $"kontext-probe-{Guid.NewGuid():N}", "embeddings.meta.cache");

	[Test]
	public async Task LoadOrProbe_Probes_Once_Then_Reuses_The_Cache() {
		var cachePath = TempCachePath();
		try {
			var generator = new ProbeGenerator("test-model", dimensions: 7);

			var first = await VectorIndexMetadata.LoadOrProbeAsync(generator, "Test", cachePath, CancellationToken.None);
			var second = await VectorIndexMetadata.LoadOrProbeAsync(generator, "Test", cachePath, CancellationToken.None);

			await Assert.That(first).IsEqualTo(second);
			await Assert.That(generator.CallCount).IsEqualTo(1); // second boot made no probe call
		} finally { Directory.Delete(Path.GetDirectoryName(cachePath)!, recursive: true); }
	}

	[Test]
	public async Task LoadOrProbe_Reprobes_When_The_Model_Changes() {
		var cachePath = TempCachePath();
		try {
			await VectorIndexMetadata.LoadOrProbeAsync(
				new ProbeGenerator("old-model", dimensions: 7), "Test", cachePath, CancellationToken.None);

			var changed = new ProbeGenerator("new-model", dimensions: 9);
			var meta = await VectorIndexMetadata.LoadOrProbeAsync(changed, "Test", cachePath, CancellationToken.None);

			await Assert.That(changed.CallCount).IsEqualTo(1);
			await Assert.That(meta.Model).IsEqualTo("new-model");
			await Assert.That(meta.Dimensions).IsEqualTo(9);

			// and the new result replaced the cache
			await Assert.That(VectorIndexMetadata.Load(cachePath)).IsEqualTo(meta);
		} finally { Directory.Delete(Path.GetDirectoryName(cachePath)!, recursive: true); }
	}

	[Test]
	public async Task LoadOrProbe_Reprobes_When_The_Cache_Is_Corrupt() {
		var cachePath = TempCachePath();
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
			await File.WriteAllTextAsync(cachePath, "not json");

			var generator = new ProbeGenerator("test-model", dimensions: 7);
			var meta = await VectorIndexMetadata.LoadOrProbeAsync(generator, "Test", cachePath, CancellationToken.None);

			await Assert.That(generator.CallCount).IsEqualTo(1);
			await Assert.That(meta.Dimensions).IsEqualTo(7);
		} finally { Directory.Delete(Path.GetDirectoryName(cachePath)!, recursive: true); }
	}

	[Test]
	public async Task Source_Throws_Until_Probed() {
		var source = new VectorIndexMetadataSource();
		await Assert.That(() => source.Value).Throws<InvalidOperationException>();

		source.Value = new VectorIndexMetadata("Test", "test-model", 7);
		await Assert.That(source.Value.Dimensions).IsEqualTo(7);
	}

	sealed class ProbeGenerator(string? modelId, int dimensions) : IEmbeddingGenerator<string, Embedding<float>> {
		public int CallCount;
		public Exception? Failure;

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
			CancellationToken cancellationToken = default) {
			CallCount++;
			if (Failure != null)
				throw Failure;
			var result = new GeneratedEmbeddings<Embedding<float>>();
			foreach (var _ in values)
				result.Add(new Embedding<float>(new float[dimensions]));
			return Task.FromResult(result);
		}

		public object? GetService(Type serviceType, object? serviceKey = null) =>
			serviceType == typeof(EmbeddingGeneratorMetadata)
				? new EmbeddingGeneratorMetadata(defaultModelId: modelId)
				: null;

		public void Dispose() { }
	}
}
