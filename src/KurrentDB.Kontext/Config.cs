// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace KurrentDB.Kontext;

public static class JsonOptions {
	public static readonly JsonSerializerOptions Compact = new() {
		WriteIndented = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new RawJsonBytesConverter() },
	};
}

internal sealed class RawJsonBytesConverter : JsonConverter<ReadOnlyMemory<byte>> {
	public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		throw new NotSupportedException($"{nameof(RawJsonBytesConverter)} is write-only");
	}

	public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte> value, JsonSerializerOptions options) {
		if (value.IsEmpty)
			writer.WriteNullValue();
		else
			writer.WriteRawValue(value.Span, skipInputValidation: true);
	}
}

public class KontextStorageConfig {
	public string DataPath { get; set; } = "";

	public void Validate() {
		if (string.IsNullOrWhiteSpace(DataPath))
			throw new ArgumentException("Storage:DataPath must not be empty.");
	}
}

public enum EmbeddingsProvider {
	Local, // In-process ONNX (all-MiniLM-L6-v2). 384-dim, CPU-only, no API key required.
	OpenAI,
	Ollama,
	GoogleVertexAI,
	AmazonBedrock,
}

public class KontextEmbeddingsConfig {
	public EmbeddingsProvider Provider { get; set; } = EmbeddingsProvider.Local;

	public LocalEmbeddingsConfig Local { get; set; } = new();
	public OpenAIEmbeddingsConfig OpenAI { get; set; } = new();
	public OllamaEmbeddingsConfig Ollama { get; set; } = new();
	public GoogleVertexAIEmbeddingsConfig GoogleVertexAI { get; set; } = new();
	public AmazonBedrockEmbeddingsConfig AmazonBedrock { get; set; } = new();

	public int BatchSize => Provider switch {
		EmbeddingsProvider.Local => Local.BatchSize,
		EmbeddingsProvider.OpenAI => OpenAI.BatchSize,
		EmbeddingsProvider.Ollama => Ollama.BatchSize,
		EmbeddingsProvider.GoogleVertexAI => GoogleVertexAI.BatchSize,
		EmbeddingsProvider.AmazonBedrock => AmazonBedrock.BatchSize,
		_ => 100,
	};
}

public class LocalEmbeddingsConfig {
	// ONNX supports batching but it spikes CPU for little gain.
	public int BatchSize { get; set; } = 1;
}

public class OpenAIEmbeddingsConfig {
	public string? ApiKey { get; set; }

	public string Model { get; set; } = "text-embedding-3-small";

	// Optional — set for OpenAI-compatible proxies.
	public string? Endpoint { get; set; }

	public int BatchSize { get; set; } = 256;
}

public class OllamaEmbeddingsConfig {
	public string Endpoint { get; set; } = "http://localhost:11434";

	public string Model { get; set; } = "nomic-embed-text";

	public int BatchSize { get; set; } = 16;
}

public class GoogleVertexAIEmbeddingsConfig {
	public string? ProjectId { get; set; }

	public string? Region { get; set; }

	public string Model { get; set; } = "text-embedding-004";

	public int BatchSize { get; set; } = 100;
}

public class AmazonBedrockEmbeddingsConfig {
	public string? Region { get; set; }

	public string Model { get; set; } = "amazon.titan-embed-text-v2:0";

	public int BatchSize { get; set; } = 96;
}