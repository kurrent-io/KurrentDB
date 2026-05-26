// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace KurrentDB.Kontext.Indexing;

/// <summary>
/// Metadata stored alongside a USearch index to identify the embedding provider and model
/// used to populate it. Used to detect mismatches across runs (which would corrupt search
/// results). A null <paramref name="Model"/> means the provider's default model — some
/// providers' generators don't expose the model id.
/// </summary>
public record VectorIndexMetadata(
	[property: JsonPropertyName("provider")] string Provider,
	[property: JsonPropertyName("model")] string? Model,
	[property: JsonPropertyName("dimensions")] int Dimensions) {

	public static async Task<VectorIndexMetadata> LoadOrProbeAsync(
		IEmbeddingGenerator<string, Embedding<float>> generator, string provider, string cachePath,
		CancellationToken ct) {
		var model = ModelOf(generator);

		if (File.Exists(cachePath)) {
			try {
				var cached = Load(cachePath);
				if (cached.Provider == provider && cached.Model == model && cached.Dimensions > 0)
					return cached;
			} catch (InvalidDataException) {
				// unreadable cache — fall through and re-probe
			}
		}

		var probed = await ProbeAsync(generator, provider, ct);
		Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
		probed.Save(cachePath);
		return probed;
	}

	public static async Task<VectorIndexMetadata> ProbeAsync(
		IEmbeddingGenerator<string, Embedding<float>> generator, string provider, CancellationToken ct) {
		var model = ModelOf(generator);

		Embedding<float> probe;
		try {
			probe = await generator.GenerateAsync("kontext startup probe", cancellationToken: ct);
		} catch (Exception ex) when (ex is not OperationCanceledException) {
			throw new InvalidOperationException(
				$"The embedding generation probe failed for provider '{provider}' " +
				$"(model '{model ?? "default"}'). Check the Kontext Embeddings configuration, " +
				"credentials and connectivity.", ex);
		}

		if (probe.Vector.Length <= 0) {
			throw new InvalidOperationException(
				$"The embedding generation probe for provider '{provider}' " +
				$"(model '{model ?? "default"}') returned an empty vector.");
		}

		return new VectorIndexMetadata(provider, model, probe.Vector.Length);
	}

	static string? ModelOf(IEmbeddingGenerator<string, Embedding<float>> generator) {
		var meta = generator.GetService(typeof(EmbeddingGeneratorMetadata)) as EmbeddingGeneratorMetadata;
		return string.IsNullOrEmpty(meta?.DefaultModelId) ? null : meta.DefaultModelId;
	}

	public void Save(string path) {
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, JsonSerializer.Serialize(this));
		File.Move(tmp, path, overwrite: true);
	}

	public static VectorIndexMetadata Load(string path) {
		if (!File.Exists(path))
			throw new FileNotFoundException(
				$"Vector index metadata not found at '{path}'.", path);

		try {
			return JsonSerializer.Deserialize<VectorIndexMetadata>(File.ReadAllText(path))!;
		} catch (JsonException ex) {
			throw new InvalidDataException($"Failed to parse vector index metadata at '{path}': {ex.Message}", ex);
		}
	}
}

public sealed class VectorIndexMetadataSource {
	VectorIndexMetadata? _value;

	public VectorIndexMetadata Value {
		get => _value ?? throw new InvalidOperationException("The embedding generator has not been probed yet.");
		set => _value = value;
	}
}
