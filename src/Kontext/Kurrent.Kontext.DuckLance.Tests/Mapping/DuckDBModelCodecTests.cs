using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using TUnit.Assertions.Enums;

namespace DuckLance.Tests.Mapping;

/// <summary>
/// Pure unit tests for <see cref="DuckDBModelCodec{TRecord}"/>: the model-driven codec built to
/// supersede the record mapper. No DuckDB connection; reads are driven by
/// <see cref="FakeDbDataReader"/> feeding the validated wire shapes, POSITIONALLY per the codec law.
/// </summary>
public class DuckDBModelCodecTests {
    [Test]
    public async Task Encode_Places_Values_In_Model_Order_And_Passes_Collections_Through() {
        var codec = new DuckDBModelCodec<CodecRecord>(BuildModel<CodecRecord>());

        var record = new CodecRecord {
            Id      = "m1",
            Content = "Sergio lives in Norway",
            Rank    = 7,
            Tags    = ["subject:sergio", "place:norway"],
            Blob    = [1, 2, 3],
            Vec     = new([1f, 2f, 3f, 4f])
        };

        float[] vector = [9f, 8f, 7f, 6f];
        var     values = codec.Encode(record, new VectorSlots(["vec"], [vector]));

        // Model-property order: id, content, rank, tags, blob, vec.
        await Assert.That((string)values[0]!).IsEqualTo("m1");
        await Assert.That((string)values[1]!).IsEqualTo("Sergio lives in Norway");
        await Assert.That((int)values[2]!).IsEqualTo(7);

        // Collections and blobs pass through by reference — no normalization on the write path.
        await Assert.That(ReferenceEquals(values[3], record.Tags)).IsTrue();
        await Assert.That(ReferenceEquals(values[4], record.Blob)).IsTrue();

        // The vector slot takes the pipeline-supplied wire vector, not the record's own value.
        await Assert.That(ReferenceEquals(values[5], vector)).IsTrue();
    }

    [Test]
    public async Task Encode_Null_Vector_Binds_Null() {
        var codec  = new DuckDBModelCodec<CodecRecord>(BuildModel<CodecRecord>());
        var values = codec.Encode(new() { Id = "m1" }, new VectorSlots(["vec"], [null]));

        await Assert.That(values[5]).IsNull();
    }

    [Test]
    public async Task Decode_Full_Projection_Reads_Positionally() {
        var codec = new DuckDBModelCodec<CodecRecord>(BuildModel<CodecRecord>());

        // Column order IS model-property order — the codec never looks at column names.
        using var reader = FakeDbDataReader.SingleRow(
            ("id", "m1"),
            ("content", "hello"),
            ("rank", 42),
            ("tags", new List<string> { "a", "b" }),
            ("blob", new byte[] { 9, 8 }),
            ("vec", new List<float> { 1f, 2f, 3f, 4f }));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That(record.Id).IsEqualTo("m1");
        await Assert.That(record.Content).IsEqualTo("hello");
        await Assert.That(record.Rank).IsEqualTo(42);
        await Assert.That(record.Tags).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);
        await Assert.That(record.Blob).IsEquivalentTo(new byte[] { 9, 8 }, CollectionOrdering.Matching);
        await Assert.That(record.Vec.ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Decode_Lean_Projection_Shifts_Positions_Past_The_Absent_Vector() {
        // The vector property sits in the MIDDLE of this model, so the lean projection shifts every
        // later column's position — the strongest test of the precomputed lean layout.
        var codec = new DuckDBModelCodec<VectorInMiddleRecord>(BuildModel<VectorInMiddleRecord>());

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "m1"),
            ("name", "after-the-vector"));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: false);

        await Assert.That(record.Id).IsEqualTo("m1");
        await Assert.That(record.Name).IsEqualTo("after-the-vector");
        await Assert.That(record.Vec.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Decode_DBNull_Sets_Null_Or_Leaves_Default() {
        var codec = new DuckDBModelCodec<NullableCodecRecord>(BuildModel<NullableCodecRecord>());

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "m1"),
            ("maybe", DBNull.Value),
            ("count", DBNull.Value));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That(record.Maybe).IsNull();
        await Assert.That(record.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Decode_Coerces_DateTime_To_DateTimeOffset_As_Utc() {
        var codec = new DuckDBModelCodec<TimestampCodecRecord>(BuildModel<TimestampCodecRecord>());

        var clockReading = new DateTime(
            2026, 7, 19,
            12, 30, 0,
            DateTimeKind.Unspecified);

        using var reader = FakeDbDataReader.SingleRow(("id", "m1"), ("created", clockReading));
        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That(record.Created).IsEqualTo(new DateTimeOffset(clockReading, TimeSpan.Zero));
    }

    [Test]
    public async Task Decode_Reads_Blob_From_Stream() {
        // DuckDB.NET returns a populated BLOB as a Stream (UnmanagedMemoryStream); the codec must
        // materialize it — Convert.ChangeType would throw on the non-IConvertible stream.
        var codec = new DuckDBModelCodec<BlobCodecRecord>(BuildModel<BlobCodecRecord>());

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "m1"),
            ("blob", new MemoryStream([5, 6, 7])));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That(record.Blob).IsEquivalentTo(new byte[] { 5, 6, 7 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Dynamic_Records_Encode_And_Decode_By_ModelName() {
        var definition = new VectorStoreCollectionDefinition {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Name", typeof(string)),
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 3)
            ]
        };

        var model = new DuckDBModelBuilder().BuildDynamic(definition, null);
        var codec = new DuckDBModelCodec<Dictionary<string, object?>>(model);

        float[] vector = [1f, 2f, 3f];
        var values = codec.Encode(new() { ["Id"] = "k", ["Name"] = "n", ["Vec"] = null }, new VectorSlots(["vec"], [vector]));

        await Assert.That((string)values[0]!).IsEqualTo("k");
        await Assert.That((string)values[1]!).IsEqualTo("n");
        await Assert.That(ReferenceEquals(values[2], vector)).IsTrue();

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "k"),
            ("name", "n"),
            ("vec", new List<float> { 1f, 2f, 3f }));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That((string)record["Id"]!).IsEqualTo("k");
        await Assert.That((string)record["Name"]!).IsEqualTo("n");
        await Assert.That(((ReadOnlyMemory<float>)record["Vec"]!).ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f }, CollectionOrdering.Matching);
    }

    // Ported from the retired mapper tests: every supported scalar type + List<string> tags + all
    // three native vector CLR shapes. Extraction covers ReadOnlyMemory<float>, Embedding<float>,
    // and float[] (which passes through by reference); Encode places each slot at its column.
    [Test]
    public async Task Encode_Projects_All_Scalar_Types_And_Extracted_Vectors_In_Model_Order() {
        var model = BuildModel<AllTypesRecord>();
        var codec = new DuckDBModelCodec<AllTypesRecord>(model);

        var blob = new byte[] { 1, 2, 3 };

        var dateTime = new DateTime(
            2024, 5, 6,
            7, 8, 9,
            DateTimeKind.Utc);

        var dateTimeOffset = new DateTimeOffset(
            2024, 5, 6,
            7, 8, 9,
            TimeSpan.FromHours(2));

        var record = new AllTypesRecord {
            Id                = "key-1",
            BoolVal           = true,
            ShortVal          = 7,
            IntVal            = 42,
            LongVal           = 123L,
            FloatVal          = 1.5f,
            DoubleVal         = 2.5d,
            DecimalVal        = 9.99m,
            StringVal         = "hello",
            DateTimeVal       = dateTime,
            DateTimeOffsetVal = dateTimeOffset,
            BlobVal           = blob,
            Tags              = ["a", "b"],
            VecRom            = new([1f, 2f, 3f, 4f]),
            VecEmb            = new(new[] { 5f, 6f, 7f, 8f }),
            VecArr            = [9f, 10f, 11f, 12f]
        };

        var slots  = await codec.VectorizeAsync(record);
        var values = codec.Encode(record, slots);

        // The array must have exactly one slot per model property, in model order.
        await Assert.That(values.Length).IsEqualTo(model.Properties.Count);

        // Key + scalars pass through unchanged.
        await Assert.That((string)ValueFor(model, values, "Id")!).IsEqualTo("key-1");
        await Assert.That((bool)ValueFor(model, values, "BoolVal")!).IsTrue();
        await Assert.That((short)ValueFor(model, values, "ShortVal")!).IsEqualTo((short)7);
        await Assert.That((int)ValueFor(model, values, "IntVal")!).IsEqualTo(42);
        await Assert.That((long)ValueFor(model, values, "LongVal")!).IsEqualTo(123L);
        await Assert.That((float)ValueFor(model, values, "FloatVal")!).IsEqualTo(1.5f);
        await Assert.That((double)ValueFor(model, values, "DoubleVal")!).IsEqualTo(2.5d);
        await Assert.That((decimal)ValueFor(model, values, "DecimalVal")!).IsEqualTo(9.99m);
        await Assert.That((string)ValueFor(model, values, "StringVal")!).IsEqualTo("hello");
        await Assert.That((DateTime)ValueFor(model, values, "DateTimeVal")!).IsEqualTo(dateTime);
        await Assert.That((DateTimeOffset)ValueFor(model, values, "DateTimeOffsetVal")!).IsEqualTo(dateTimeOffset);

        // byte[] and List<string> pass through by reference — validated bind shapes, no normalization.
        await Assert.That(ReferenceEquals(ValueFor(model, values, "BlobVal"), blob)).IsTrue();
        await Assert.That(ReferenceEquals(ValueFor(model, values, "Tags"), record.Tags)).IsTrue();

        // Vector shapes materialize to float[]; a float[] source passes through by reference.
        await AssertIsFloatArray(ValueFor(model, values, "VecRom"), [1f, 2f, 3f, 4f]);
        await AssertIsFloatArray(ValueFor(model, values, "VecEmb"), [5f, 6f, 7f, 8f]);
        await Assert.That(ReferenceEquals(ValueFor(model, values, "VecArr"), record.VecArr)).IsTrue();
    }

    // Ported from the retired mapper tests: validated wire shapes -> declared CLR shapes, for every
    // scalar type and all three vector CLR shapes.
    [Test]
    public async Task Decode_Reads_All_Wire_Shapes_Into_Declared_Types() {
        var codec = new DuckDBModelCodec<AllTypesRecord>(BuildModel<AllTypesRecord>());

        var blob = new byte[] { 9, 8, 7 };

        var dateTime = new DateTime(
            2023, 1, 2,
            3, 4, 5,
            DateTimeKind.Utc);

        var dateTimeOffset = new DateTimeOffset(
            2023, 1, 2,
            3, 4, 5,
            TimeSpan.FromHours(-3));

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "abc"),
            ("boolval", true),
            ("shortval", (short)7),
            ("intval", 42),
            ("longval", 123L),
            ("floatval", 1.5f),
            ("doubleval", 2.5d),
            ("decimalval", 9.99m),
            ("stringval", "hello"),
            ("datetimeval", dateTime),
            ("datetimeoffsetval", dateTimeOffset),
            ("blobval", blob),
            ("tags", new List<string> { "a", "b" }),
            ("vecrom", new List<float> { 1f, 2f, 3f, 4f }),
            ("vecemb", new List<float> { 5f, 6f, 7f, 8f }),
            ("vecarr", new List<float> { 9f, 10f, 11f, 12f }));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That(record.Id).IsEqualTo("abc");
        await Assert.That(record.BoolVal).IsTrue();
        await Assert.That(record.ShortVal).IsEqualTo((short)7);
        await Assert.That(record.IntVal).IsEqualTo(42);
        await Assert.That(record.LongVal).IsEqualTo(123L);
        await Assert.That(record.FloatVal).IsEqualTo(1.5f);
        await Assert.That(record.DoubleVal).IsEqualTo(2.5d);
        await Assert.That(record.DecimalVal).IsEqualTo(9.99m);
        await Assert.That(record.StringVal).IsEqualTo("hello");
        await Assert.That(record.DateTimeVal).IsEqualTo(dateTime);
        await Assert.That(record.DateTimeOffsetVal).IsEqualTo(dateTimeOffset);
        await Assert.That(record.BlobVal).IsEquivalentTo(blob, CollectionOrdering.Matching);
        await Assert.That(record.Tags).IsEquivalentTo(new List<string> { "a", "b" }, CollectionOrdering.Matching);

        // ReadOnlyMemory<float>, Embedding<float>, and float[] all recover the exact element values.
        await Assert.That(record.VecRom.ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f }, CollectionOrdering.Matching);
        await Assert.That(record.VecEmb.Vector.ToArray()).IsEquivalentTo(new[] { 5f, 6f, 7f, 8f }, CollectionOrdering.Matching);
        await Assert.That(record.VecArr).IsEquivalentTo(new[] { 9f, 10f, 11f, 12f }, CollectionOrdering.Matching);
    }

    // Ported from the retired mapper tests: an array-typed data property (string[]) passes through
    // Encode by reference and materializes back as string[] from the wire List<string>.
    [Test]
    public async Task ArrayDataProperty_Encodes_By_Reference_And_Decodes_To_Array() {
        var model = BuildModel<ArrayTagsRecord>();
        var codec = new DuckDBModelCodec<ArrayTagsRecord>(model);

        var record = new ArrayTagsRecord { Id = "k", Tags = ["x", "y", "z"] };
        var values = codec.Encode(record, default);

        await Assert.That(ReferenceEquals(ValueFor(model, values, "Tags"), record.Tags)).IsTrue();

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "k"),
            ("tags", new List<string> { "x", "y", "z" }));

        reader.Read();

        var roundTripped = codec.Decode(reader, includeVectors: true);

        await Assert.That(roundTripped.Tags.GetType()).IsEqualTo(typeof(string[]));
        await Assert.That(roundTripped.Tags).IsEquivalentTo(new[] { "x", "y", "z" }, CollectionOrdering.Matching);
    }

    // Ported from the retired mapper tests: dynamic dictionaries are keyed by ModelName while the
    // wire uses StorageName — including an explicit "name_col" override.
    [Test]
    public async Task Dynamic_Records_Respect_StorageName_Overrides_Both_Directions() {
        var definition = new VectorStoreCollectionDefinition {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Name", typeof(string)) { StorageName = "name_col" },
                new VectorStoreDataProperty("Tags", typeof(List<string>)),
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 3)
            ]
        };

        var model = new DuckDBModelBuilder().BuildDynamic(definition, null);
        var codec = new DuckDBModelCodec<Dictionary<string, object?>>(model);

        // In: the record dictionary is read by ModelName; the vector slot is addressed by StorageName.
        var record = new Dictionary<string, object?> {
            ["Id"]   = "k",
            ["Name"] = "n",
            ["Tags"] = new List<string> { "a", "b" },
            ["Vec"]  = new ReadOnlyMemory<float>([1f, 2f, 3f])
        };

        var slots  = await codec.VectorizeAsync(record);
        var values = codec.Encode(record, slots);

        await Assert.That((string)ValueFor(model, values, "Id")!).IsEqualTo("k");
        await Assert.That((string)ValueFor(model, values, "Name")!).IsEqualTo("n");
        await Assert.That((List<string>)ValueFor(model, values, "Tags")!).IsEquivalentTo(new List<string> { "a", "b" }, CollectionOrdering.Matching);
        await AssertIsFloatArray(ValueFor(model, values, "Vec"), [1f, 2f, 3f]);

        // Out: the reader columns sit in model-property order under their storage names; the
        // resulting dictionary is keyed by ModelName ("Name"), never by StorageName ("name_col").
        using var reader = FakeDbDataReader.SingleRow(
            ("id", "k"),
            ("name_col", "n"),
            ("tags", new List<string> { "a", "b" }),
            ("vec", new List<float> { 1f, 2f, 3f }));

        reader.Read();

        var mapped = codec.Decode(reader, includeVectors: true);

        await Assert.That(mapped.ContainsKey("Name")).IsTrue();
        await Assert.That(mapped.ContainsKey("name_col")).IsFalse();
        await Assert.That((string)mapped["Id"]!).IsEqualTo("k");
        await Assert.That((string)mapped["Name"]!).IsEqualTo("n");
        await Assert.That((List<string>)mapped["Tags"]!).IsEquivalentTo(new List<string> { "a", "b" }, CollectionOrdering.Matching);
        await Assert.That(mapped["Vec"]!.GetType()).IsEqualTo(typeof(ReadOnlyMemory<float>));
        await Assert.That(((ReadOnlyMemory<float>)mapped["Vec"]!).ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task MultiVector_Encode_Places_Each_Named_Slot_At_Its_Own_Column() {
        var codec = new DuckDBModelCodec<MultiVectorRecord>(BuildModel<MultiVectorRecord>());

        float[] vecA = [1f, 2f, 3f, 4f];
        float[] vecB = [5f, 6f, 7f, 8f];

        var values = codec.Encode(new() { Id = "m1", Name = "n" }, new VectorSlots(["veca", "vecb"], [vecA, vecB]));

        // Model-property order: id, veca, name, vecb — each slot lands at ITS column, found by
        // storage name, never by slot ordering.
        await Assert.That((string)values[0]!).IsEqualTo("m1");
        await Assert.That(ReferenceEquals(values[1], vecA)).IsTrue();
        await Assert.That((string)values[2]!).IsEqualTo("n");
        await Assert.That(ReferenceEquals(values[3], vecB)).IsTrue();
    }

    [Test]
    public async Task MultiVector_Decode_Full_Projection_Reads_Both_Vectors() {
        var codec = new DuckDBModelCodec<MultiVectorRecord>(BuildModel<MultiVectorRecord>());

        using var reader = FakeDbDataReader.SingleRow(
            ("id", "m1"),
            ("veca", new List<float> { 1f, 2f, 3f, 4f }),
            ("name", "n"),
            ("vecb", new List<float> { 5f, 6f, 7f, 8f }));

        reader.Read();

        var record = codec.Decode(reader, includeVectors: true);

        await Assert.That(record.Id).IsEqualTo("m1");
        await Assert.That(record.VecA.ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f }, CollectionOrdering.Matching);
        await Assert.That(record.Name).IsEqualTo("n");
        await Assert.That(record.VecB.ToArray()).IsEquivalentTo(new[] { 5f, 6f, 7f, 8f }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task MultiVector_Lean_Projection_Shifts_Positions_Past_Every_Absent_Vector() {
        // TWO vector columns interleaved with data columns: the lean layout must skip both holes.
        var codec = new DuckDBModelCodec<MultiVectorRecord>(BuildModel<MultiVectorRecord>());

        using var reader = FakeDbDataReader.SingleRow(("id", "m1"), ("name", "n"));
        reader.Read();

        var record = codec.Decode(reader, includeVectors: false);

        await Assert.That(record.Id).IsEqualTo("m1");
        await Assert.That(record.Name).IsEqualTo("n");
        await Assert.That(record.VecA.Length).IsEqualTo(0);
        await Assert.That(record.VecB.Length).IsEqualTo(0);
    }

    [Test]
    public async Task VectorizeAsync_NativeVector_Extracts_Without_A_Generator() {
        // No generator supplied: a native-vector model vectorizes by extraction, not generation.
        var codec  = new DuckDBModelCodec<CodecRecord>(BuildModel<CodecRecord>());
        var record = new CodecRecord { Id = "m1", Vec = new([1f, 2f, 3f, 4f]) };

        var slots = await codec.VectorizeAsync(record);

        await Assert.That(slots.Count).IsEqualTo(1);
        await Assert.That(slots["vec"]!).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task VectorizeAsync_Routes_Native_Extraction_And_Dispatcher_Generation_Per_Slot() {
        var generator = new RecordingEmbeddingGenerator();
        var codec     = new DuckDBModelCodec<MixedVectorRecord>(BuildModel<MixedVectorRecord>(generator));

        var slots = await codec.VectorizeAsync(
            new() {
                Id        = "m1",
                NativeVec = new([1f, 2f, 3f, 4f]),
                Text      = "raw text to embed"
            });

        // The native slot is extracted synchronously; the text slot went through MEVD's own
        // per-property dispatcher (backed here by the recording generator: vector = (call, position)).
        await Assert.That(slots.Count).IsEqualTo(2);
        await Assert.That(slots["nativevec"]!).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f }, CollectionOrdering.Matching);
        await Assert.That(slots["text"]!).IsEquivalentTo(new[] { 0f, 0f }, CollectionOrdering.Matching);
        await Assert.That(generator.Calls.Count).IsEqualTo(1);
        await Assert.That(generator.Calls[0]).IsEquivalentTo(["raw text to embed"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task VectorizeBatchAsync_Generates_Once_Per_Text_Property_For_The_Whole_Batch() {
        var generator = new RecordingEmbeddingGenerator();
        var codec     = new DuckDBModelCodec<MixedVectorRecord>(BuildModel<MixedVectorRecord>(generator));

        var slots = await codec.VectorizeBatchAsync([
            new() { Id = "m1", NativeVec = new([1f, 0f, 0f, 0f]), Text = "first" },
            new() { Id = "m2", NativeVec = new([0f, 1f, 0f, 0f]), Text = "second" }
        ]);

        // ONE dispatcher call for the text property covering the whole batch; the native property
        // never touches the generator at all.
        await Assert.That(generator.Calls.Count).IsEqualTo(1);
        await Assert.That(generator.Calls[0]).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);

        await Assert.That(slots.Length).IsEqualTo(2);
        await Assert.That(slots[0]["nativevec"]!).IsEquivalentTo(new[] { 1f, 0f, 0f, 0f }, CollectionOrdering.Matching);
        await Assert.That(slots[0]["text"]!).IsEquivalentTo(new[] { 0f, 0f }, CollectionOrdering.Matching);
        await Assert.That(slots[1]["nativevec"]!).IsEquivalentTo(new[] { 0f, 1f, 0f, 0f }, CollectionOrdering.Matching);
        await Assert.That(slots[1]["text"]!).IsEquivalentTo(new[] { 0f, 1f }, CollectionOrdering.Matching);
    }

    static CollectionModel BuildModel<TRecord>(IEmbeddingGenerator? generator = null) =>
        new DuckDBModelBuilder().Build(
            typeof(TRecord), typeof(string), null,
            generator);

    // Finds the encoded value for a property by its ModelName — the values array itself is
    // positional (model-property order), which is exactly what this helper hides from assertions.
    static object? ValueFor(CollectionModel model, object?[] values, string modelName) {
        for (var i = 0; i < model.Properties.Count; i++) {
            if (model.Properties[i].ModelName == modelName)
                return values[i];
        }

        throw new InvalidOperationException($"No property named '{modelName}' in the model.");
    }

    static async Task AssertIsFloatArray(object? value, float[] expected) {
        await Assert.That(value!.GetType()).IsEqualTo(typeof(float[]));
        await Assert.That((float[])value).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    #region Test records

    sealed class AllTypesRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public bool BoolVal { get; set; }

        [VectorStoreData] public short ShortVal { get; set; }

        [VectorStoreData] public int IntVal { get; set; }

        [VectorStoreData] public long LongVal { get; set; }

        [VectorStoreData] public float FloatVal { get; set; }

        [VectorStoreData] public double DoubleVal { get; set; }

        [VectorStoreData] public decimal DecimalVal { get; set; }

        [VectorStoreData] public string StringVal { get; set; } = "";

        [VectorStoreData] public DateTime DateTimeVal { get; set; }

        [VectorStoreData] public DateTimeOffset DateTimeOffsetVal { get; set; }

        [VectorStoreData] public byte[] BlobVal { get; set; } = [];

        [VectorStoreData] public List<string> Tags { get; set; } = [];

        [VectorStoreVector(4)] public ReadOnlyMemory<float> VecRom { get; set; }

        [VectorStoreVector(4)] public Embedding<float> VecEmb { get; set; } = new(ReadOnlyMemory<float>.Empty);

        [VectorStoreVector(4)] public float[] VecArr { get; set; } = [];
    }

    sealed class ArrayTagsRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string[] Tags { get; set; } = [];
    }

    sealed class CodecRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Content { get; set; } = "";

        [VectorStoreData] public int Rank { get; set; }

        [VectorStoreData] public List<string> Tags { get; set; } = [];

        [VectorStoreData] public byte[] Blob { get; set; } = [];

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class VectorInMiddleRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }

        [VectorStoreData] public string Name { get; set; } = "";
    }

    sealed class NullableCodecRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public int? Maybe { get; set; }

        [VectorStoreData] public int Count { get; set; }
    }

    sealed class TimestampCodecRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public DateTimeOffset Created { get; set; }
    }

    sealed class BlobCodecRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public byte[] Blob { get; set; } = [];
    }

    sealed class MultiVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> VecA { get; set; }

        [VectorStoreData] public string Name { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> VecB { get; set; }
    }

    sealed class MixedVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> NativeVec { get; set; }

        // Generation input: a string-typed vector property only passes model validation when a
        // generator is resolved for it, so tests build this model with the recording generator.
        // Dimensions match the recording generator's two-float (call, position) vectors.
        [VectorStoreVector(2)] public string Text { get; set; } = "";
    }

    #endregion
}
