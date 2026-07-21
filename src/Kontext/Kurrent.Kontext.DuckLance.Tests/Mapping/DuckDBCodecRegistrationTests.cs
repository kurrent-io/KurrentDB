using System.Data.Common;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using TUnit.Assertions.Enums;

namespace DuckLance.Tests.Mapping;

/// <summary>
/// End-to-end proof of the codec resolution rule: a codec registered on
/// <see cref="DuckDBVectorStoreOptions.Codecs"/> wins over the model-driven default — including
/// surviving the store's defensive options copy. The stamping codec marks each direction so the
/// assertions can tell exactly which path produced the value.
/// </summary>
[LanceRequired]
public class DuckDBCodecRegistrationTests {
    [Test]
    public async Task Registered_Codec_Drives_Both_Directions_Of_A_Real_Collection() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            var options = new DuckDBVectorStoreOptions { DatabasePath = Path.Combine(dir, "duck.db") };
            options.Codecs.Add(new StampingCodec());

            store = new(options);

            var collection = store.GetCollection<string, StampRecord>("stamps");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
                new StampRecord {
                    Id   = "s1",
                    Note = "note",
                    Vec  = new([1f, 0f, 0f, 0f])
                });

            var got = await collection.GetAsync("s1", new() { IncludeVectors = true });

            await Assert.That(got).IsNotNull();

            // "+enc" proves the registered codec's Encode wrote the row; "+dec" proves its Decode
            // read it back — the model-codec default would have produced plain "note".
            await Assert.That(got!.Note).IsEqualTo("note+enc+dec");
            await Assert.That(got.Vec.ToArray()).IsEquivalentTo(new[] { 1f, 0f, 0f, 0f }, CollectionOrdering.Matching);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-codec-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDeleteDir(string dir) {
        try {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        } catch (IOException) {
            // Best-effort cleanup.
        } catch (UnauthorizedAccessException) {
            // Best-effort cleanup.
        }
    }

    sealed class StampRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Note { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    // A precomputed-vector codec: vectors come off the record, no generator anywhere — so both
    // vectorize modes are overridden and the text hook is never consulted.
    sealed class StampingCodec() : SingleVectorRecordCodec<StampRecord>(embeddingGenerator: null) {
        protected override string GetVectorText(StampRecord record) => throw new NotSupportedException();

        public override object?[] Encode(StampRecord record, float[]? vector) => [record.Id, record.Note + "+enc", vector];

        public override StampRecord Decode(DbDataReader reader, bool includeVectors) => new() {
            Id   = reader.GetString(0),
            Note = reader.GetString(1) + "+dec",
            Vec  = includeVectors ? new([.. reader.GetFieldValue<List<float>>(2)]) : default
        };

        public override ValueTask<VectorSlots> VectorizeAsync(StampRecord record, CancellationToken ct = default) =>
            ValueTask.FromResult(new VectorSlots([""], [record.Vec.ToArray()]));

        public override ValueTask<VectorSlots[]> VectorizeBatchAsync(IReadOnlyList<StampRecord> records, CancellationToken ct = default) =>
            ValueTask.FromResult(records.Select(record => new VectorSlots([""], [record.Vec.ToArray()])).ToArray());
    }
}
