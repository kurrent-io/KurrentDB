// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.AI;

namespace KurrentDB.Kontext.Embeddings;

/// <summary>
/// Embedding generator for Cohere embed models on Amazon Bedrock. The AWS MEAI extension
/// marshals Titan-format request bodies only, so Cohere's request format (texts, input_type)
/// is marshalled here. Cohere's embeddings are asymmetric: documents are embedded with
/// input_type 'search_document' (the default) and search queries with 'search_query'
/// (<see cref="EmbeddingPurpose.QueryOptions"/>).
/// </summary>
internal sealed class CohereBedrockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
	// Cohere's embed API accepts at most 96 texts per request.
	const int MaxTextsPerRequest = 96;

	readonly Func<InvokeModelRequest, CancellationToken, Task<InvokeModelResponse>> _invoke;
	readonly Action _dispose;
	readonly string _modelId;
	readonly EmbeddingGeneratorMetadata _metadata;

	public CohereBedrockEmbeddingGenerator(IAmazonBedrockRuntime runtime, string modelId)
		: this(runtime.InvokeModelAsync, runtime.Dispose, modelId) {
	}

	// Test seam: the request/response types are plain DTOs, but IAmazonBedrockRuntime is
	// too large to fake by hand.
	internal CohereBedrockEmbeddingGenerator(
		Func<InvokeModelRequest, CancellationToken, Task<InvokeModelResponse>> invoke,
		Action dispose,
		string modelId) {
		_invoke = invoke;
		_dispose = dispose;
		_modelId = modelId;
		_metadata = new EmbeddingGeneratorMetadata("bedrock-cohere", defaultModelId: modelId);
	}

	public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
		IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
		CancellationToken cancellationToken = default) {
		var isQuery = options?.AdditionalProperties?.TryGetValue(EmbeddingPurpose.Key, out string? purpose) == true
			&& purpose == EmbeddingPurpose.Query;

		var result = new GeneratedEmbeddings<Embedding<float>>();
		foreach (var chunk in values.Chunk(MaxTextsPerRequest)) {
			var body = JsonSerializer.SerializeToUtf8Bytes(new {
				texts = chunk,
				input_type = isQuery ? "search_query" : "search_document",
				// Cohere rejects over-long inputs by default; truncate instead.
				truncate = "END",
			});

			var response = await _invoke(new InvokeModelRequest {
				ModelId = _modelId,
				ContentType = "application/json",
				Accept = "application/json",
				Body = new MemoryStream(body),
			}, cancellationToken).ConfigureAwait(false);

			ParseEmbeddings(response.Body.ToArray(), chunk.Length, result);
		}

		return result;
	}

	void ParseEmbeddings(byte[] json, int expectedCount, GeneratedEmbeddings<Embedding<float>> result) {
		var reader = new Utf8JsonReader(json);
		List<float>? scratch = null;

		while (reader.Read()) {
			if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName
				|| !reader.ValueTextEquals("embeddings"u8))
				continue;

			if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
				throw Malformed("'embeddings' is not an array");

			var count = 0;
			while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
				if (reader.TokenType != JsonTokenType.StartArray)
					throw Malformed("expected an array of vectors");

				scratch ??= new List<float>(1024);
				scratch.Clear();
				while (reader.Read() && reader.TokenType == JsonTokenType.Number)
					scratch.Add(reader.GetSingle());

				result.Add(new Embedding<float>(scratch.ToArray()));
				count++;
			}

			if (count != expectedCount) {
				throw new InvalidOperationException(
					$"Model '{_modelId}' returned {count} embeddings for {expectedCount} texts.");
			}

			return;
		}

		throw Malformed("missing 'embeddings'");

		InvalidOperationException Malformed(string detail) =>
			new($"Model '{_modelId}' returned a malformed response: {detail}.");
	}

	public object? GetService(Type serviceType, object? serviceKey = null) =>
		serviceType == typeof(EmbeddingGeneratorMetadata) && serviceKey is null ? _metadata : null;

	public void Dispose() => _dispose();
}
