// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Retain decides per memory, so it embeds one content per call rather than a batch. This pins the
/// invariant that makes that safe: a text embedded alone must produce the SAME vector as the same
/// text embedded alongside others. If it did not, a memory's distance would depend on the shape of
/// the call that stored it, and every threshold in retain would rest on how the caller batched.
///
/// A timing comparison used to live here and has been removed. The local generator runs one ONNX
/// session per string whichever way it is called (SentencePieceOnnxEmbeddingGenerator.GenerateAsync
/// is a plain foreach), so the two shapes differ only by N-1 list allocations against a multi-
/// millisecond inference. Three attempts to measure that produced three different signs, because
/// sustained ONNX load throttles the machine by more than the effect being measured.
///
/// The remote generators (OpenAI, Bedrock, Vertex, Ollama) DO batch over HTTP, where N calls cost N
/// round trips. None of them run in this suite, so that remains a source-read claim.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public class EmbeddingBatchEquivalenceTests {
	static readonly string[] Corpus = [
		"the test runner lives at scripts/testing/test-runner.cs",
		"the projector checkpoints after the batch lands",
		"KontextMemoryWriter batches every statement into one command",
		"the memories table stores log_position with a BTREE index",
		"recall embeds content and nothing else",
		"retain mints every memory id on the server",
		"the certificate rotation job runs every ninety days",
		"gossip timeouts default to two seconds in cluster mode",
		"the janitor skips tables whose row count did not move",
		"OpenTelemetry traces are exported over OTLP gRPC",
		"the admin UI listens on the same port as the gRPC surface",
		"the schema was reset rather than migrated",
		"a colleague reported the outage during standup",
		"we abandoned the second index because it cost too much disk",
		"the writer never mutates rows the projector owns",
		"the build broke after the dependency bump",
	];

	[Test]
	public async ValueTask a_text_embeds_the_same_alone_as_it_does_inside_a_batch(CancellationToken cancellationToken) {
		// Arrange
		using var embeddings = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };

		// Act
		var together = await embeddings.GenerateAsync(Corpus, options, cancellationToken);

		// Assert
		for (var i = 0; i < Corpus.Length; i++) {
			var alone = await embeddings.GenerateAsync([Corpus[i]], options, cancellationToken);

			await Assert.That(alone[0].Vector.ToArray()).IsEquivalentTo(together[i].Vector.ToArray());
		}
	}
}
