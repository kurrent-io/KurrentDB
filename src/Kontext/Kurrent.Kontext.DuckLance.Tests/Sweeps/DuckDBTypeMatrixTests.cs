using System.Data.Common;
using DuckDB.NET.Data;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using TUnit.Assertions.Enums;

namespace DuckLance.Tests.Sweeps;

/// <summary>
/// Stage 11 §15 validation sweep: a single POCO carrying every data type the DuckLance provider claims to
/// support (see <see cref="Kurrent.SemanticKernel.Connectors.DuckLance"/>'s <c>DuckDBModelBuilder.SupportedDataTypes</c>),
/// exercised through a full round trip (<c>EnsureCollectionExistsAsync</c> → <c>UpsertAsync</c> → <c>GetAsync</c>
/// with <c>IncludeVectors = true</c> → vector search) against a real DuckDB + <c>lance</c> connection.
/// </summary>
/// <remarks>
/// <para>
/// Two records are round-tripped: <see cref="CreatePopulatedRecord"/> (every field, including every nullable
/// field, has a non-null value) and <see cref="CreateWithNullsRecord"/> (identical shape, but every nullable
/// field — <c>int?</c>, <c>double?</c>, <c>bool?</c> — is explicitly <see langword="null"/>, while every
/// non-nullable field still carries a distinct value so cross-record value bleed would be caught).
/// </para>
/// <para>
/// <b>Findings captured as executable facts (see the individual assertions below for exact figures):</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>decimal</b> round-trips exactly at the column's declared <c>DECIMAL(38,18)</c> precision/scale: an 18
/// fractional-digit value (the scale's own maximum) survives the trip with no truncation or rounding.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>DateTime</b> (a <c>TIMESTAMP</c> column, no timezone) round-trips its wall-clock <see cref="DateTime.Ticks"/>
/// exactly, but does NOT preserve <see cref="DateTime.Kind"/>: DuckDB.NET's <c>TIMESTAMP</c> reader value comes
/// back as a plain naive value, so a property written as <see cref="DateTimeKind.Utc"/> reads back as
/// <see cref="DateTimeKind.Unspecified"/>. This is a real, user-visible limitation, not a workaround-in-hiding —
/// see the dedicated assertion and comment below.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>DateTimeOffset</b> (a <c>TIMESTAMP WITH TIME ZONE</c> column) round-trips the absolute UTC instant exactly,
/// but does NOT preserve the original <see cref="DateTimeOffset.Offset"/>: Lance/DuckDB normalizes
/// <c>TIMESTAMPTZ</c> to a UTC instant, and <c>DuckDBModelCodec.CoerceScalarFromStorage</c> reconstructs the
/// value with <see cref="TimeSpan.Zero"/> offset regardless of what was written (e.g. writing with a +05:00 or
/// -08:00 offset both come back with <c>Offset == TimeSpan.Zero</c>). See the dedicated assertion below.
/// </description>
/// </item>
/// <item>
/// <description><b>string</b> and every list/array element type tested round-trip exactly, with no observed data loss.</description>
/// </item>
/// <item>
/// <description>
/// <b>byte[]</b> (a <c>BLOB</c> column) round-trips exactly, including populated payloads containing <c>0x00</c>
/// and <c>0xFF</c> bytes. This was previously a connector bug, now FIXED in
/// <c>DuckDBModelCodec.CoerceBlobFromStorage</c>: DuckDB.NET's ADO.NET reader returns a populated <c>BLOB</c>
/// column's value from <c>GetValue()</c> as a <see cref="System.IO.UnmanagedMemoryStream"/> (its declared
/// <c>FieldType</c> is <see cref="System.IO.Stream"/>), never as <c>byte[]</c>. The old generic fallback
/// (<c>Convert.ChangeType(value, targetType, ...)</c>) required the source value to implement
/// <see cref="IConvertible"/>, which <see cref="System.IO.Stream"/> does not, so <c>GetAsync</c>/<c>SearchAsync</c>
/// used to throw <see cref="InvalidCastException"/> ("Object must implement IConvertible.") for the ENTIRE row the
/// moment it reached a non-null <c>byte[]</c> column. The mapper now detects the declared <c>byte[]</c> type and
/// reads the returned <see cref="System.IO.Stream"/> fully into a byte array (an empty BLOB that already arrives
/// as <c>byte[]</c> passes straight through). <see cref="CreatePopulatedRecord"/> therefore carries a real
/// <c>BytesVal</c> payload that is asserted on the round trip, and a dedicated test —
/// <see cref="BytesVal_PopulatedBlobColumn_RoundTripsExactlyViaGetAsyncAndSearchAsync"/> — exercises a larger
/// payload spanning every byte value (including <c>0x00</c>/<c>0xFF</c>) through BOTH <c>GetAsync</c> and
/// <c>SearchAsync</c>, and additionally proves via a direct SQL read that the WRITE path was always correct.
/// </description>
/// </item>
/// </list>
/// </remarks>
[LanceRequired]
public class DuckDBTypeMatrixTests {
    [Test]
    public async Task FullRoundTrip_PopulatedAndWithNullsRecords_EveryValueMatchesExactly() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, TypeMatrixRecord>("typematrix");
            await collection.EnsureCollectionExistsAsync();

            var populated = CreatePopulatedRecord();
            var withNulls = CreateWithNullsRecord();

            await collection.UpsertAsync([populated, withNulls]);

            var gotPopulated = await collection.GetAsync("populated", new() { IncludeVectors = true });
            var gotWithNulls = await collection.GetAsync("withnulls", new() { IncludeVectors = true });

            await Assert.That(gotPopulated).IsNotNull();
            await Assert.That(gotWithNulls).IsNotNull();

            await AssertMatches(populated, gotPopulated!, false);
            await AssertMatches(withNulls, gotWithNulls!, true);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task VectorSearch_ReturnsDataPropertiesIdenticallyToGetAsync() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, TypeMatrixRecord>("typematrix");
            await collection.EnsureCollectionExistsAsync();

            var populated = CreatePopulatedRecord();
            var withNulls = CreateWithNullsRecord();

            await collection.UpsertAsync([populated, withNulls]);

            // Query aligned with `populated.Vec` ([1,0,0,0]): it must come back first (distance 0 under the
            // default null-distance-function -> cosine-distance convention), `withNulls.Vec` ([0,1,0,0]) second.
            var results = new List<VectorSearchResult<TypeMatrixRecord>>();

            await foreach (var result in collection.SearchAsync(
                               new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]),
                               2,
                               new() { IncludeVectors = true }))
                results.Add(result);

            await Assert.That(results.Count).IsEqualTo(2);

            var fromSearchPopulated = results.Single(r => r.Record.Id == "populated").Record;
            var fromSearchWithNulls = results.Single(r => r.Record.Id == "withnulls").Record;

            // Vector search flows through the exact same DuckDBModelCodec.Decode as GetAsync, so the
            // data-property assertions are identical to the round-trip test above.
            await AssertMatches(populated, fromSearchPopulated, false);
            await AssertMatches(withNulls, fromSearchWithNulls, true);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    /// <summary>
    /// Exercises the <c>byte[]</c>/<c>BLOB</c> fix described in this class's remarks: a larger payload spanning
    /// every byte value (including <c>0x00</c> and <c>0xFF</c>) round-trips EXACTLY through both <c>GetAsync</c>
    /// and <c>SearchAsync</c> (both flow through <c>DuckDBModelCodec.CoerceBlobFromStorage</c>, which reads
    /// DuckDB.NET's returned <see cref="System.IO.Stream"/> fully into a byte array), and a direct SQL read
    /// (bypassing the codec entirely) confirms the same bytes were persisted.
    /// </summary>
    [Test]
    public async Task BytesVal_PopulatedBlobColumn_RoundTripsExactlyViaGetAsyncAndSearchAsync() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, TypeMatrixRecord>("typematrix");
            await collection.EnsureCollectionExistsAsync();

            // A few hundred bytes covering every value 0x00..0xFF (so both 0x00 and 0xFF are exercised) plus a
            // trailing 0xFF, to catch any length/terminator handling error in the stream-to-byte[] read.
            var bytes = BuildBlobPayload();

            var record = CreatePopulatedRecord();
            record.Id       = "bytesprobe";
            record.BytesVal = bytes;

            await collection.UpsertAsync(record);

            // 1) GetAsync round-trips the populated BLOB exactly (previously this threw InvalidCastException for
            //    the whole row -- see this class's <remarks> for the fixed root cause).
            var got = await collection.GetAsync("bytesprobe", new() { IncludeVectors = true });
            await Assert.That(got).IsNotNull();
            await Assert.That(got!.BytesVal).IsNotNull();
            await Assert.That(got.BytesVal!).IsEquivalentTo(bytes, CollectionOrdering.Matching);

            // 2) SearchAsync (same DuckDBModelCodec.Decode path) returns the BLOB identically.
            var results = new List<VectorSearchResult<TypeMatrixRecord>>();

            await foreach (var result in collection.SearchAsync(
                               record.Vec,
                               1,
                               new() { IncludeVectors = true }))
                results.Add(result);

            await Assert.That(results.Count).IsEqualTo(1);
            var fromSearch = results.Single(r => r.Record.Id == "bytesprobe").Record;
            await Assert.That(fromSearch.BytesVal).IsNotNull();
            await Assert.That(fromSearch.BytesVal!).IsEquivalentTo(bytes, CollectionOrdering.Matching);

            // 3) The WRITE path is (and always was) correct: read the raw bytes back with a direct SQL query
            //    (using System.IO.Stream, DuckDB.NET's actual runtime type for a BLOB value) to confirm.
            var storedBytes = await ReadBytesValDirectAsync(store, collection.QualifiedTableName, "bytesprobe");
            await Assert.That(storedBytes).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    /// <summary>Builds a 512-byte BLOB payload covering every byte value 0x00..0xFF (0x00 and 0xFF included), with a trailing 0xFF.</summary>
    static byte[] BuildBlobPayload() {
        var bytes = new byte[512];

        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 256);

        bytes[^1] = 0xFF;
        return bytes;
    }

    /// <summary>Reads the <c>bytes_val</c> column for <paramref name="id"/> directly via SQL, bypassing the codec entirely.</summary>
    static async Task<byte[]> ReadBytesValDirectAsync(DuckDBVectorStore store, string qualifiedTableName, string id) =>
        await store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT bytes_val FROM {qualifiedTableName} WHERE id = ?";
                command.Parameters.Add(new DuckDBParameter(id));

                using var reader = command.ExecuteReader();
                reader.Read();

                using var stream = (Stream)reader.GetValue(0);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            },
            CancellationToken.None);

    /// <summary>Asserts every property of <paramref name="got"/> against the expected <paramref name="expected"/> record.</summary>
    static async Task AssertMatches(TypeMatrixRecord expected, TypeMatrixRecord got, bool expectNulls) {
        await Assert.That(got.Id).IsEqualTo(expected.Id);
        await Assert.That(got.BoolVal).IsEqualTo(expected.BoolVal);
        await Assert.That(got.ShortVal).IsEqualTo(expected.ShortVal);
        await Assert.That(got.IntVal).IsEqualTo(expected.IntVal);
        await Assert.That(got.LongVal).IsEqualTo(expected.LongVal);
        await Assert.That(got.FloatVal).IsEqualTo(expected.FloatVal);
        await Assert.That(got.DoubleVal).IsEqualTo(expected.DoubleVal);

        // FINDING: decimal round-trips at full DECIMAL(38,18) precision -- no truncation of the 18 fractional digits.
        await Assert.That(got.DecimalVal).IsEqualTo(expected.DecimalVal);

        await Assert.That(got.StringVal).IsEqualTo(expected.StringVal);

        // FINDING: DateTime ticks round-trip exactly (TIMESTAMP has no timezone, so the wall-clock value is
        // stored verbatim), but Kind is NOT preserved: a value written as DateTimeKind.Utc/Local reads back as
        // DateTimeKind.Unspecified, because DuckDB.NET's TIMESTAMP reader value carries no Kind metadata. This is
        // asserted explicitly (rather than relying on DateTime.Equals, which ignores Kind) so the limitation stays
        // visible instead of silently passing.
        await Assert.That(got.DateTimeVal.Ticks).IsEqualTo(expected.DateTimeVal.Ticks);
        await Assert.That(got.DateTimeVal.Kind).IsEqualTo(DateTimeKind.Unspecified);

        // FINDING: DateTimeOffset round-trips its absolute UTC instant exactly, but the original Offset is NOT
        // preserved: TIMESTAMP WITH TIME ZONE normalizes to a UTC instant, and DuckDBModelCodec reconstructs
        // the DateTimeOffset with Offset == TimeSpan.Zero regardless of what was written (here, +05:00 for the
        // populated record and -08:00 for the withNulls record are both flattened to +00:00).
        await Assert.That(got.DateTimeOffsetVal.UtcDateTime).IsEqualTo(expected.DateTimeOffsetVal.UtcDateTime);
        await Assert.That(got.DateTimeOffsetVal.Offset).IsEqualTo(TimeSpan.Zero);

        // FINDING: byte[]/BLOB round-trips exactly now (see this class's <remarks> and
        // BytesVal_PopulatedBlobColumn_RoundTripsExactlyViaGetAsyncAndSearchAsync). The populated record carries a
        // real payload (asserted byte-for-byte); the with-nulls record leaves BytesVal null (the DBNull read path).
        if (expected.BytesVal is null)
            await Assert.That(got.BytesVal).IsNull();
        else {
            await Assert.That(got.BytesVal).IsNotNull();
            await Assert.That(got.BytesVal!).IsEquivalentTo(expected.BytesVal, CollectionOrdering.Matching);
        }

        if (expectNulls) {
            await Assert.That(got.NullableIntVal).IsNull();
            await Assert.That(got.NullableDoubleVal).IsNull();
            await Assert.That(got.NullableBoolVal).IsNull();
        } else {
            await Assert.That(got.NullableIntVal).IsEqualTo(expected.NullableIntVal);
            await Assert.That(got.NullableDoubleVal).IsEqualTo(expected.NullableDoubleVal);
            await Assert.That(got.NullableBoolVal).IsEqualTo(expected.NullableBoolVal);
        }

        await Assert.That(got.StringListVal).IsEquivalentTo(expected.StringListVal, CollectionOrdering.Matching);
        await Assert.That(got.StringArrayVal).IsEquivalentTo(expected.StringArrayVal, CollectionOrdering.Matching);
        await Assert.That(got.IntListVal).IsEquivalentTo(expected.IntListVal, CollectionOrdering.Matching);
        await Assert.That(got.DoubleListVal).IsEquivalentTo(expected.DoubleListVal, CollectionOrdering.Matching);
        await Assert.That(got.DoubleArrayVal).IsEquivalentTo(expected.DoubleArrayVal, CollectionOrdering.Matching);

        await Assert.That(got.Vec.ToArray()).IsEquivalentTo(expected.Vec.ToArray(), CollectionOrdering.Matching);
    }

    static TypeMatrixRecord CreatePopulatedRecord() =>
        new() {
            Id         = "populated",
            BoolVal    = true,
            ShortVal   = 12345,
            IntVal     = 123456789,
            LongVal    = 1234567890123456789L,
            FloatVal   = 3.14159265f,
            DoubleVal  = 2.718281828459045,
            DecimalVal = 123456789.123456789012345678m, // 18 fractional digits: exactly the column's DECIMAL(38,18) scale.
            StringVal  = "hello type matrix — unicode: café",
            DateTimeVal = new(
                2024, 3, 15,
                10, 30, 45,
                123, DateTimeKind.Utc),
            DateTimeOffsetVal = new(
                2024, 3, 15,
                10, 30, 45,
                123, TimeSpan.FromHours(5)),

            // Populated byte[] payload (incl. 0x00 and 0xFF) -- round-trips exactly; see this class's <remarks> and
            // BytesVal_PopulatedBlobColumn_RoundTripsExactlyViaGetAsyncAndSearchAsync.
            BytesVal          = [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x7F, 0xFF, 0x10, 0x20, 0x30],
            NullableIntVal    = 42,
            NullableDoubleVal = 3.5,
            NullableBoolVal   = true,
            StringListVal     = ["alpha", "beta", "gamma"],
            StringArrayVal    = ["x", "y", "z"],
            IntListVal        = [1, 2, 3, 4],
            DoubleListVal     = [1.1, 2.2, 3.3],
            DoubleArrayVal    = [9.9, 8.8, 7.7],
            Vec               = new([1f, 0f, 0f, 0f])
        };

    static TypeMatrixRecord CreateWithNullsRecord() =>
        new() {
            Id         = "withnulls",
            BoolVal    = false,
            ShortVal   = -12345,
            IntVal     = -123456789,
            LongVal    = -1234567890123456789L,
            FloatVal   = -3.14159265f,
            DoubleVal  = -2.718281828459045,
            DecimalVal = 987654321.987654321987654321m, // Also exactly 18 fractional digits, distinct from the populated record's value.
            StringVal  = "second record — no nulls in required fields",
            DateTimeVal = new(
                1999, 12, 31,
                23, 59, 59,
                999, DateTimeKind.Utc),
            DateTimeOffsetVal = new(
                1999, 12, 31,
                23, 59, 59,
                999, TimeSpan.FromHours(-8)),

            // Deliberately null: keeps coverage of the DBNull read path for a nullable BLOB column (the populated
            // record covers the non-null byte[] round trip).
            BytesVal          = null,
            NullableIntVal    = null,
            NullableDoubleVal = null,
            NullableBoolVal   = null,
            StringListVal     = ["delta", "epsilon"],
            StringArrayVal    = ["p", "q"],
            IntListVal        = [10, 20],
            DoubleListVal     = [4.4, 5.5],
            DoubleArrayVal    = [6.6],
            Vec               = new([0f, 1f, 0f, 0f])
        };

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-typematrix-test-" + Guid.NewGuid().ToString("N"));
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

    // Storage (column) names are deliberately lowercase; see the note in DuckDBCollectionCrudTests about the
    // lance extension's broken DELETE predicate pushdown for uppercase column names. This test never deletes,
    // but the convention is kept for consistency across the suite.
    sealed class TypeMatrixRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "bool_val")]
        public bool BoolVal { get; set; }

        [VectorStoreData(StorageName = "short_val")]
        public short ShortVal { get; set; }

        [VectorStoreData(StorageName = "int_val")]
        public int IntVal { get; set; }

        [VectorStoreData(StorageName = "long_val")]
        public long LongVal { get; set; }

        [VectorStoreData(StorageName = "float_val")]
        public float FloatVal { get; set; }

        [VectorStoreData(StorageName = "double_val")]
        public double DoubleVal { get; set; }

        [VectorStoreData(StorageName = "decimal_val")]
        public decimal DecimalVal { get; set; }

        [VectorStoreData(StorageName = "string_val")]
        public string StringVal { get; set; } = "";

        [VectorStoreData(StorageName = "datetime_val")]
        public DateTime DateTimeVal { get; set; }

        [VectorStoreData(StorageName = "datetimeoffset_val")]
        public DateTimeOffset DateTimeOffsetVal { get; set; }

        [VectorStoreData(StorageName = "bytes_val")]
        public byte[]? BytesVal { get; set; }

        [VectorStoreData(StorageName = "nullable_int_val")]
        public int? NullableIntVal { get; set; }

        [VectorStoreData(StorageName = "nullable_double_val")]
        public double? NullableDoubleVal { get; set; }

        [VectorStoreData(StorageName = "nullable_bool_val")]
        public bool? NullableBoolVal { get; set; }

        [VectorStoreData(StorageName = "string_list_val")]
        public List<string> StringListVal { get; set; } = [];

        [VectorStoreData(StorageName = "string_array_val")]
        public string[] StringArrayVal { get; set; } = [];

        [VectorStoreData(StorageName = "int_list_val")]
        public List<int> IntListVal { get; set; } = [];

        [VectorStoreData(StorageName = "double_list_val")]
        public List<double> DoubleListVal { get; set; } = [];

        [VectorStoreData(StorageName = "double_array_val")]
        public double[] DoubleArrayVal { get; set; } = [];

        [VectorStoreVector(4, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}