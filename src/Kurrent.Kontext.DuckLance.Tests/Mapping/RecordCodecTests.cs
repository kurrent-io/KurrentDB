using System.Data.Common;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using TUnit.Assertions.Enums;

namespace DuckLance.Tests.Mapping;

/// <summary>
/// Pure unit tests for the <see cref="RecordCodec{TRecord}"/> base and the
/// <see cref="SingleVectorRecordCodec{TRecord}"/> tier: the default vectorize modes (single and
/// batch flatten/reshape) and the sealed single-vector routing. Driven by
/// <see cref="RecordingEmbeddingGenerator"/> because the base's one-call batching contract is
/// unobservable with a real model.
/// </summary>
public class RecordCodecTests {
    [Test]
    public async Task VectorizeAsync_Embeds_Every_Slot_In_One_Call_Addressed_By_Column() {
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
    public async Task VectorizeBatchAsync_Flattens_All_Records_Into_One_Call_And_Reshapes_Back() {
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
    public async Task SingleVector_Tier_Routes_Slots_Through_The_Simple_Encode() {
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
    public async Task VectorizeAsync_Without_Generator_Throws() {
        // MEVD's model builder already rejects string-typed vector properties when the MODEL has no
        // generator, so the no-generator throw is a base-codec concern: a hand-written codec whose
        // text path runs without one must fail loudly instead of null-referencing.
        var codec = new NoGeneratorCodec();

        await Assert
            .That(async () => await codec.VectorizeAsync(new MyMemoryEntry("m1", "hello", [])))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task VectorizeBatchAsync_Without_Generator_Throws() {
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

    sealed class ArticleCodec(IEmbeddingGenerator<string, Embedding<float>> embedder) : RecordCodec<Article>(embedder) {
        protected override VectorText[] GetVectorTexts(Article record) => [new("title_vec", record.Title), new("body_vec", record.Body)];

        public override object?[] Encode(Article record, VectorSlots vectors) =>
            [record.Id, record.Title, vectors["title_vec"], record.Body, vectors["body_vec"]];

        public override Article Decode(DbDataReader reader, bool includeVectors) => throw new NotSupportedException();
    }

    #endregion
}
