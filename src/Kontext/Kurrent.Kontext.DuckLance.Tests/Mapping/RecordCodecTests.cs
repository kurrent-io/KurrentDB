using System.Data.Common;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using TUnit.Assertions.Enums;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace DuckLance.Tests.Mapping;

/// <summary>
/// Pure unit tests for the <see cref="RecordCodec{TRecord}"/> base and the
/// <see cref="SingleVectorRecordCodec{TRecord}"/> tier: the default vectorize modes (single and
/// batch flatten/reshape) and the sealed single-vector routing. Driven by
/// <see cref="RecordingEmbeddingGenerator"/> because the base's one-call batching contract is
/// unobservable with a real model.
/// </summary>
[Category("Mapping")]
public class RecordCodecTests {
    [Test]
    public async ValueTask vectorize_async_embeds_every_slot_in_one_call_addressed_by_column() {
        var generator = new RecordingEmbeddingGenerator();
        var codec     = new ArticleCodec(generator);

        var slots = await codec.VectorizeAsync(new("a1", "the title", "the body"));

        await Assert.That(generator.Calls.Count).IsEqualTo(1);
        await Assert.That(generator.Calls[0]).IsEquivalentTo(["the title", "the body"], CollectionOrdering.Matching);

        // The fake's vectors encode (call, position): title was input 0 of call 0, body input 1.
        await Assert.That(slots.Count).IsEqualTo(2);
        await Assert.That(slots["title_vec"]!).IsEquivalentTo(new[] { 0f, 0f }, CollectionOrdering.Matching);
        await Assert.That(slots["body_vec"]!).IsEquivalentTo(new[] { 0f, 1f }, CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask vectorize_batch_async_flattens_all_records_into_one_call_and_reshapes_back() {
        var generator = new RecordingEmbeddingGenerator();
        var codec     = new ArticleCodec(generator);

        var slots = await codec.VectorizeBatchAsync([
            new("a1", "t1", "b1"),
            new("a2", "t2", "b2"),
            new("a3", "t3", "b3")
        ]);

        // ONE generator call for the whole batch — 3 records × 2 slots flattened in record order.
        // Degrading to a call per record (or per slot) is the regression this test exists to catch.
        await Assert.That(generator.Calls.Count).IsEqualTo(1);
        await Assert.That(generator.Calls[0]).IsEquivalentTo(["t1", "b1", "t2", "b2", "t3", "b3"], CollectionOrdering.Matching);

        // Each slot got ITS OWN embedding back: record r's title sits at flat position 2r, its body at 2r+1.
        await Assert.That(slots.Length).IsEqualTo(3);
        await Assert.That(slots[0]["title_vec"]!).IsEquivalentTo(new[] { 0f, 0f }, CollectionOrdering.Matching);
        await Assert.That(slots[1]["body_vec"]!).IsEquivalentTo(new[] { 0f, 3f }, CollectionOrdering.Matching);
        await Assert.That(slots[2]["title_vec"]!).IsEquivalentTo(new[] { 0f, 4f }, CollectionOrdering.Matching);
        await Assert.That(slots[2]["body_vec"]!).IsEquivalentTo(new[] { 0f, 5f }, CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask single_vector_tier_routes_slots_through_the_simple_encode() {
        var generator = new RecordingEmbeddingGenerator();
        var codec     = new MemoryCodec(generator);
        var record    = new MyMemoryEntry("m1", "Sergio lives in Norway", ["subject:sergio"]);

        var slots = await codec.VectorizeAsync(record);

        // One anonymous slot; the tier never exposes column names.
        await Assert.That(slots.Count).IsEqualTo(1);
        await Assert.That(slots.Single!).IsEquivalentTo(new[] { 0f, 0f }, CollectionOrdering.Matching);

        // The slot-based Encode seals to Encode(record, slots.Single): the lone vector lands where
        // the hand-written codec placed its vector column.
        var values = codec.Encode(record, slots);

        await Assert.That(ReferenceEquals(values[3], slots.Single)).IsTrue();
    }

    [Test]
    public async ValueTask vectorize_async_without_generator_throws() {
        // MEVD's model builder already rejects string-typed vector properties when the MODEL has no
        // generator, so the no-generator throw is a base-codec concern: a hand-written codec whose
        // text path runs without one must fail loudly instead of null-referencing.
        var codec = new NoGeneratorCodec();

        await Assert
            .That(async () => await codec.VectorizeAsync(new MyMemoryEntry("m1", "hello", [])))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask vectorize_batch_async_without_generator_throws() {
        var codec = new NoGeneratorCodec();

        await Assert
            .That(async () => await codec.VectorizeBatchAsync([new MyMemoryEntry("m1", "hello", [])]))
            .Throws<InvalidOperationException>();
    }

    sealed class NoGeneratorCodec() : SingleVectorRecordCodec<MyMemoryEntry>(embeddingGenerator: null) {
        protected override string GetVectorText(MyMemoryEntry record) => record.Content;

        public override object?[] Encode(MyMemoryEntry record, float[]? vector) => [record.MemoryId, record.Content, record.Tags, vector];

        public override MyMemoryEntry Decode(DbDataReader reader, bool includeVectors) => throw new NotSupportedException();
    }

    #region Multi-vector reference

    // The design's multi-vector reference shape: the vector↔column association is written in code
    // at both ends (GetVectorTexts and Encode), never implied by ordering.
    readonly record struct Article(string Id, string Title, string Body);

    sealed class ArticleCodec(EmbeddingGenerator embedder) : RecordCodec<Article>(embedder) {
        protected override VectorText[] GetVectorTexts(Article record) => [new("title_vec", record.Title), new("body_vec", record.Body)];

        public override object?[] Encode(Article record, VectorSlots vectors) =>
            [record.Id, record.Title, vectors["title_vec"], record.Body, vectors["body_vec"]];

        public override Article Decode(DbDataReader reader, bool includeVectors) => throw new NotSupportedException();
    }

    #endregion
}
