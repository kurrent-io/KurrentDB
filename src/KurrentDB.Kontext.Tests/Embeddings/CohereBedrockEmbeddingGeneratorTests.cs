// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Amazon.BedrockRuntime.Model;

namespace KurrentDB.Kontext.Tests.Embeddings;

/// <summary>
/// Tests for the Cohere-on-Bedrock generator's request marshalling: the AWS MEAI extension
/// only speaks Titan's request format, so Cohere's (texts, input_type) is marshalled by hand.
/// </summary>
public class CohereBedrockEmbeddingGeneratorTests {
	const string ModelId = "cohere.embed-english-v3";

	/// <summary>Captures requests and answers each with one embedding per text.</summary>
	sealed class FakeBedrock {
		public readonly List<JsonDocument> Requests = [];

		public Task<InvokeModelResponse> Invoke(InvokeModelRequest request, CancellationToken ct) {
			var body = JsonDocument.Parse(request.Body);
			Requests.Add(body);

			var count = body.RootElement.GetProperty("texts").GetArrayLength();
			var embeddings = Enumerable.Range(0, count).Select(i => new[] { (float)i, 0.5f });
			var json = JsonSerializer.SerializeToUtf8Bytes(new { embeddings });
			return Task.FromResult(new InvokeModelResponse { Body = new MemoryStream(json) });
		}
	}

	static (CohereBedrockEmbeddingGenerator Generator, FakeBedrock Bedrock) Make() {
		var bedrock = new FakeBedrock();
		return (new CohereBedrockEmbeddingGenerator(bedrock.Invoke, dispose: () => { }, ModelId), bedrock);
	}

	[Test]
	public async Task Marshals_Texts_With_Document_Input_Type_By_Default() {
		var (generator, bedrock) = Make();

		await generator.GenerateAsync(["first text", "second text"]);

		var root = bedrock.Requests.Single().RootElement;
		await Assert.That(root.GetProperty("texts").EnumerateArray().Select(t => t.GetString()!).ToArray())
			.IsEquivalentTo(new[] { "first text", "second text" });
		await Assert.That(root.GetProperty("input_type").GetString()).IsEqualTo("search_document");
		await Assert.That(root.GetProperty("truncate").GetString()).IsEqualTo("END");
	}

	[Test]
	public async Task Marshals_Queries_With_Query_Input_Type() {
		var (generator, bedrock) = Make();

		await generator.GenerateAsync(["what happened?"], EmbeddingPurpose.QueryOptions);

		var root = bedrock.Requests.Single().RootElement;
		await Assert.That(root.GetProperty("input_type").GetString()).IsEqualTo("search_query");
	}

	[Test]
	public async Task Parses_One_Embedding_Per_Text() {
		var (generator, _) = Make();

		var result = await generator.GenerateAsync(["a", "b", "c"]);

		await Assert.That(result.Count).IsEqualTo(3);
		await Assert.That(result[2].Vector.ToArray()).IsEquivalentTo(new[] { 2f, 0.5f });
	}

	[Test]
	public async Task Chunks_Batches_Beyond_The_Cohere_Request_Limit() {
		var (generator, bedrock) = Make();
		var texts = Enumerable.Range(0, 100).Select(i => $"text {i}").ToArray();

		var result = await generator.GenerateAsync(texts);

		await Assert.That(result.Count).IsEqualTo(100);
		await Assert.That(bedrock.Requests.Count).IsEqualTo(2); // 96 + 4
		await Assert.That(bedrock.Requests[0].RootElement.GetProperty("texts").GetArrayLength()).IsEqualTo(96);
		await Assert.That(bedrock.Requests[1].RootElement.GetProperty("texts").GetArrayLength()).IsEqualTo(4);
	}

	[Test]
	public async Task Throws_When_The_Response_Count_Mismatches() {
		var generator = new CohereBedrockEmbeddingGenerator(
			(_, _) => Task.FromResult(new InvokeModelResponse {
				Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { embeddings = new[] { new[] { 1f } } })),
			}),
			dispose: () => { }, ModelId);

		await Assert.That(async () => { await generator.GenerateAsync(["a", "b"]); })
			.Throws<InvalidOperationException>()
			.WithMessageContaining("2 texts");
	}

	static CohereBedrockEmbeddingGenerator WithResponse(object responseBody) =>
		new((_, _) => Task.FromResult(new InvokeModelResponse {
			Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(responseBody)),
		}), dispose: () => { }, ModelId);

	[Test]
	public async Task Parses_A_Realistic_Response_With_Surrounding_Properties() {
		// Cohere responses carry other properties (some of them arrays) around 'embeddings';
		// the parser must skip them rather than mistake their contents for vectors.
		var generator = WithResponse(new {
			id = "f6d3e489",
			texts = new[] { "a", "b" },
			embeddings = new[] { new[] { 1f, 2f }, new[] { 3f, 4f } },
			response_type = "embeddings_floats",
		});

		var result = await generator.GenerateAsync(["a", "b"]);

		await Assert.That(result.Count).IsEqualTo(2);
		await Assert.That(result[1].Vector.ToArray()).IsEquivalentTo(new[] { 3f, 4f });
	}

	[Test]
	public async Task Throws_On_A_Response_Missing_Embeddings() {
		var generator = WithResponse(new { message = "throttled" });

		await Assert.That(async () => { await generator.GenerateAsync(["a"]); })
			.Throws<InvalidOperationException>()
			.WithMessageContaining("missing 'embeddings'");
	}

	[Test]
	public async Task Throws_When_Embeddings_Is_Not_An_Array() {
		var generator = WithResponse(new { embeddings = "oops" });

		await Assert.That(async () => { await generator.GenerateAsync(["a"]); })
			.Throws<InvalidOperationException>()
			.WithMessageContaining("not an array");
	}

	[Test]
	public async Task Declares_The_Model_For_The_Startup_Probe() {
		var (generator, _) = Make();

		var meta = await VectorIndexMetadata.ProbeAsync(generator, "AmazonBedrock", CancellationToken.None);

		await Assert.That(meta.Model).IsEqualTo(ModelId);
		await Assert.That(meta.Dimensions).IsEqualTo(2);
	}
}
