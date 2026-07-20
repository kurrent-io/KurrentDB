using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Embeddings;

/// <summary>
/// Integration tests for <see cref="DuckDBCollection{TKey,TRecord}"/>'s write-time embedding generation
/// (Stage 8): a vector property whose CLR type isn't natively storable (here, raw <see cref="string"/> text)
/// is generated into a real vector via a configured <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// before it's written. Each test drives a real DuckDB + <c>lance</c> connection against a temporary storage
/// directory, so the class is gated by <see cref="LanceRequiredAttribute"/>; the embedding generator itself
/// is real (see <see cref="EmbeddingModelFixture"/>), never a hand-rolled test double, so every test also
/// dynamically skips when the backing ONNX model can't be obtained (e.g. offline).
/// </summary>
[LanceRequired]
public class DuckDBEmbeddingGenerationTests {
    [Test]
    public async Task Upsert_RawText_Then_Search_RanksSemanticallyClosestFirst() {
        var fixture = await EmbeddingModelFixture.GetAsync();
        Skip.Unless(fixture.IsAvailable, "embedding model unavailable (offline?)");

        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            // Store-level generator: no per-property or per-definition generator is configured.
            store = NewStore(dir, fixture.Generator);
            var collection = store.GetCollection<string, TopicRecord>("topics");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
                new TopicRecord {
                    Id      = "dogs",
                    Content = "about dogs",
                    Text    = "Dogs are loyal companions that love to play fetch in the park."
                });

            // IncludeVectors=false round-trips fine: the property is simply left at its default, exactly like
            // any other vector property read without vectors.
            var got = await collection.GetAsync("dogs");
            await Assert.That(got).IsNotNull();
            await Assert.That(got!.Id).IsEqualTo("dogs");
            await Assert.That(got.Content).IsEqualTo("about dogs");

            // Generated-vector models can't round-trip the vector property back out as a string: the stored
            // column is a real FLOAT[384] array (not text), so asking for it back with IncludeVectors=true
            // fails rather than silently returning nonsense.
            await Assert
                .That(async () => await collection.GetAsync("dogs", new() { IncludeVectors = true }))
                .Throws<NotSupportedException>();

            await collection.UpsertAsync(
                new TopicRecord {
                    Id      = "finance",
                    Content = "about finance",
                    Text    = "The stock market rallied today as investors reacted to the interest rate decision."
                });

            await collection.UpsertAsync(
                new TopicRecord {
                    Id      = "cooking",
                    Content = "about cooking",
                    Text    = "To make a good risotto, slowly add warm broth while stirring constantly."
                });

            // The search-side generation path (Stage 5) is untouched by this stage; a raw string query drives
            // it end to end, through the same real generator used to write the records above.
            var results = await SearchToListAsync(collection.SearchAsync("puppies playing fetch with a ball", 3));

            await Assert.That(results.Count).IsEqualTo(3);
            await Assert.That(results[0].Record.Id).IsEqualTo("dogs");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Upsert_With_PropertyLevel_Generator_From_Definition_Works() {
        var fixture = await EmbeddingModelFixture.GetAsync();
        Skip.Unless(fixture.IsAvailable, "embedding model unavailable (offline?)");

        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            // No store-level generator: the property-level one, set on the definition below, is the only one
            // configured — proving the abstraction's own definition-level precedence resolves and drives
            // generation, not anything DuckLance-specific.
            store = NewStore(dir, null);

            var definition = new VectorStoreCollectionDefinition {
                Properties = [
                    new VectorStoreKeyProperty("Id", typeof(string)),
                    new VectorStoreDataProperty("Content", typeof(string)),
                    new VectorStoreVectorProperty("Text", typeof(string), EmbeddingModelFixture.Dimensions) { EmbeddingGenerator = fixture.Generator }
                ]
            };

            var collection = store.GetCollection<string, TopicRecord>("topics_propgen", definition);
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
                new TopicRecord {
                    Id      = "dogs",
                    Content = "about dogs",
                    Text    = "Dogs are loyal companions that love to play fetch in the park."
                });

            var got = await collection.GetAsync("dogs");
            await Assert.That(got).IsNotNull();
            await Assert.That(got!.Content).IsEqualTo("about dogs");

            var results = await SearchToListAsync(collection.SearchAsync("puppies playing fetch with a ball", 1));

            await Assert.That(results.Count).IsEqualTo(1);
            await Assert.That(results[0].Record.Id).IsEqualTo("dogs");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Batch_Upsert_With_Generation_All_Records_Become_Searchable() {
        var fixture = await EmbeddingModelFixture.GetAsync();
        Skip.Unless(fixture.IsAvailable, "embedding model unavailable (offline?)");

        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir, fixture.Generator);
            var collection = store.GetCollection<string, TopicRecord>("topics_batch");
            await collection.EnsureCollectionExistsAsync();

            // A single UpsertAsync(IEnumerable<TRecord>) call for all three: the collection generates each
            // vector property's embeddings once per property, for the whole batch (not once per record) —
            // this can't easily be observed without a fake generator (which the HARD RULE forbids), so the
            // behavior is instead verified indirectly: every record must be present and searchable afterwards.
            await collection.UpsertAsync(
            [
                new() {
                    Id      = "dogs",
                    Content = "about dogs",
                    Text    = "Dogs are loyal companions that love to play fetch in the park."
                },
                new() {
                    Id      = "finance",
                    Content = "about finance",
                    Text    = "The stock market rallied today as investors reacted to the interest rate decision."
                },
                new() {
                    Id      = "cooking",
                    Content = "about cooking",
                    Text    = "To make a good risotto, slowly add warm broth while stirring constantly."
                }
            ]);

            await Assert.That(await collection.GetAsync("dogs")).IsNotNull();
            await Assert.That(await collection.GetAsync("finance")).IsNotNull();
            await Assert.That(await collection.GetAsync("cooking")).IsNotNull();

            var results = await SearchToListAsync(collection.SearchAsync("quarterly earnings and interest rates", 3));

            await Assert.That(results.Count).IsEqualTo(3);
            await Assert.That(results[0].Record.Id).IsEqualTo("finance");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Mixed_Native_And_Generated_Vector_Properties_Are_Both_Searchable() {
        var fixture = await EmbeddingModelFixture.GetAsync();
        Skip.Unless(fixture.IsAvailable, "embedding model unavailable (offline?)");

        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir, fixture.Generator);
            var collection = store.GetCollection<string, MixedRecord>("mixed");
            await collection.EnsureCollectionExistsAsync();

            // One record carrying BOTH a precomputed native vector AND raw text for the generated property.
            await collection.UpsertAsync(
                new MixedRecord {
                    Id        = "r1",
                    Content   = "mixed record",
                    NativeVec = new([1f, 0f, 0f, 0f]),
                    Text      = "Dogs are loyal companions that love to play fetch in the park."
                });

            var byNative = await SearchToListAsync(
                collection.SearchAsync(
                    new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]),
                    1,
                    new() { VectorProperty = r => r.NativeVec }));

            await Assert.That(byNative.Count).IsEqualTo(1);
            await Assert.That(byNative[0].Record.Id).IsEqualTo("r1");

            var byText = await SearchToListAsync(
                collection.SearchAsync(
                    "puppies playing fetch with a ball",
                    1,
                    new() { VectorProperty = r => r.Text }));

            await Assert.That(byText.Count).IsEqualTo(1);
            await Assert.That(byText[0].Record.Id).IsEqualTo("r1");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static DuckDBVectorStore NewStore(string dir, IEmbeddingGenerator? generator) =>
        new(
            new() {
                DatabasePath       = Path.Combine(dir, "duck.db"),
                EmbeddingGenerator = generator
            });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-embed-test-" + Guid.NewGuid().ToString("N"));
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

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    sealed class TopicRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Content { get; set; } = "";

        [VectorStoreVector(EmbeddingModelFixture.Dimensions)]
        public string Text { get; set; } = "";
    }

    sealed class MixedRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Content { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> NativeVec { get; set; }

        [VectorStoreVector(EmbeddingModelFixture.Dimensions)]
        public string Text { get; set; } = "";
    }
}