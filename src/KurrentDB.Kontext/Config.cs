// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Kurrent.Kontext.Embeddings.Aws;
using Kurrent.Kontext.Embeddings.GoogleVertexAI;
using Kurrent.Kontext.Embeddings.Ollama;
using Kurrent.Kontext.Embeddings.OpenAI;
using Kurrent.Kontext.Embeddings.Prototype;

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

	public LocalEmbeddingsOptions Local { get; set; } = new();
	public OpenAIEmbeddingsOptions OpenAI { get; set; } = new();
	public OllamaEmbeddingsOptions Ollama { get; set; } = new();
	public GoogleVertexAIEmbeddingsOptions GoogleVertexAI { get; set; } = new();
	public AmazonBedrockEmbeddingsOptions AmazonBedrock { get; set; } = new();

	public int BatchSize => Provider switch {
		EmbeddingsProvider.Local => Local.BatchSize,
		EmbeddingsProvider.OpenAI => OpenAI.BatchSize,
		EmbeddingsProvider.Ollama => Ollama.BatchSize,
		EmbeddingsProvider.GoogleVertexAI => GoogleVertexAI.BatchSize,
		EmbeddingsProvider.AmazonBedrock => AmazonBedrock.BatchSize,
		_ => 100,
	};
}