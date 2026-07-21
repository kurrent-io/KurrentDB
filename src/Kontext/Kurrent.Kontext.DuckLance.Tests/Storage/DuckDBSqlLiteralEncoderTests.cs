using DuckDB.NET.Data;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Adversarial unit tests for <see cref="DuckDBSqlLiteralEncoder"/>. The pure-encoding tests need no connection and
/// run everywhere; the round-trip proofs (gated by <see cref="LanceRequiredAttribute"/>) use a real local DuckDB
/// connection as the encoder's oracle — for every type, <c>SELECT {Encode(v)}</c> must reconstruct <c>v</c> exactly.
/// </summary>
public class DuckDBSqlLiteralEncoderTests {
    // =============================================================================================================
    // NULL / bool
    // =============================================================================================================

    [Test]
    public async Task Encode_Null_IsNull() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(null)).IsEqualTo("NULL");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(DBNull.Value)).IsEqualTo("NULL");
    }

    [Test]
    [Arguments(true, "TRUE")]
    [Arguments(false, "FALSE")]
    public async Task Encode_Bool_IsKeyword(bool value, string expected) => await Assert.That(DuckDBSqlLiteralEncoder.Encode(value)).IsEqualTo(expected);

    // =============================================================================================================
    // Strings — the security-critical path.
    // =============================================================================================================

    [Test]
    [Arguments("O'Brien", "'O''Brien'")]                         // single embedded quote doubled
    [Arguments("'; DROP TABLE x; --", "'''; DROP TABLE x; --'")] // classic injection, leading quote doubled
    [Arguments("a''b", "'a''''b'")]                              // already-doubled input is doubled again
    [Arguments("", "''")]                                        // empty string
    [Arguments("plain", "'plain'")]                              // no escaping needed
    public async Task Encode_String_DoublesQuotes(string value, string expected) => await Assert.That(DuckDBSqlLiteralEncoder.Encode(value)).IsEqualTo(expected);

    [Test]
    public async Task Encode_String_PreservesNewlinesAndUnicode() {
        // Newlines are legal inside a DuckDB literal and must pass through byte-for-byte.
        await Assert.That(DuckDBSqlLiteralEncoder.Encode("line1\nline2")).IsEqualTo("'line1\nline2'");

        // Emoji (surrogate pair) and an RTL word survive unchanged.
        await Assert.That(DuckDBSqlLiteralEncoder.Encode("🦆")).IsEqualTo("'🦆'");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode("مرحبا")).IsEqualTo("'مرحبا'");
    }

    [Test]
    public async Task Encode_VeryLongString_IsNotTruncated() {
        string big     = new('a', 200_000);
        var    encoded = DuckDBSqlLiteralEncoder.Encode(big);

        // Exactly the payload plus the two surrounding quotes: nothing dropped, nothing added.
        await Assert.That(encoded.Length).IsEqualTo(big.Length + 2);
        await Assert.That(encoded[0]).IsEqualTo('\'');
        await Assert.That(encoded[^1]).IsEqualTo('\'');
    }

    [Test]
    [Arguments("a\0b")] // NUL in the middle
    [Arguments("\0")]   // lone NUL
    [Arguments("trailing\0")]
    public async Task Encode_StringWithNul_Throws(string value) => await Assert.That(() => DuckDBSqlLiteralEncoder.Encode(value)).Throws<ArgumentException>();

    // =============================================================================================================
    // Integers
    // =============================================================================================================

    [Test]
    public async Task Encode_Integers_AreBareInvariantDigits() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(0)).IsEqualTo("0");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(-5)).IsEqualTo("-5");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(int.MinValue)).IsEqualTo("-2147483648");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(long.MaxValue)).IsEqualTo("9223372036854775807");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(long.MinValue)).IsEqualTo("-9223372036854775808");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode((sbyte)-128)).IsEqualTo("-128");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode((byte)255)).IsEqualTo("255");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode((short)-32768)).IsEqualTo("-32768");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(ulong.MaxValue)).IsEqualTo("18446744073709551615");
    }

    // =============================================================================================================
    // Float / double — non-finite and negative zero.
    // =============================================================================================================

    [Test]
    public async Task Encode_Float_HandlesNonFiniteAndNegativeZero() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(float.NaN)).IsEqualTo("CAST('NaN' AS FLOAT)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(float.PositiveInfinity)).IsEqualTo("CAST('Infinity' AS FLOAT)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(float.NegativeInfinity)).IsEqualTo("CAST('-Infinity' AS FLOAT)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(-0.0f)).IsEqualTo("CAST('-0' AS FLOAT)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(0.1f)).IsEqualTo("CAST('0.1' AS FLOAT)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(float.MaxValue)).IsEqualTo("CAST('3.4028235E+38' AS FLOAT)");
    }

    [Test]
    public async Task Encode_Double_HandlesNonFiniteAndNegativeZero() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(double.NaN)).IsEqualTo("CAST('NaN' AS DOUBLE)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(double.PositiveInfinity)).IsEqualTo("CAST('Infinity' AS DOUBLE)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(double.NegativeInfinity)).IsEqualTo("CAST('-Infinity' AS DOUBLE)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(-0.0d)).IsEqualTo("CAST('-0' AS DOUBLE)");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(0.1d)).IsEqualTo("CAST('0.1' AS DOUBLE)");
    }

    // =============================================================================================================
    // Decimal / DateTime / DateTimeOffset
    // =============================================================================================================

    [Test]
    public async Task Encode_Decimal_CastsToDecimal38_18() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(123.456m)).IsEqualTo("CAST('123.456' AS DECIMAL(38,18))");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(-0.000000000000000001m)).IsEqualTo("CAST('-0.000000000000000001' AS DECIMAL(38,18))");
    }

    [Test]
    public async Task Encode_DateTime_IsMicrosecondTimestamp() {
        var dt = new DateTime(
            2024, 1, 2,
            3, 4, 5).AddTicks(1_230_000); // .123 seconds

        await Assert.That(DuckDBSqlLiteralEncoder.Encode(dt)).IsEqualTo("TIMESTAMP '2024-01-02 03:04:05.123000'");
    }

    [Test]
    public async Task Encode_DateTimeOffset_NormalizesToUtc() {
        var dto = new DateTimeOffset(
            2024, 1, 2,
            3, 4, 5,
            TimeSpan.FromMinutes(330)); // +05:30

        await Assert.That(DuckDBSqlLiteralEncoder.Encode(dto)).IsEqualTo("TIMESTAMPTZ '2024-01-01 21:34:05.000000+00:00'");
    }

    // =============================================================================================================
    // byte[] — fully hex-escaped BLOB.
    // =============================================================================================================

    [Test]
    public async Task Encode_ByteArray_IsFullyHexEscapedBlob() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new byte[] { 0xAB, 0xCD })).IsEqualTo(@"CAST('\xAB\xCD' AS BLOB)");

        // The hazardous bytes: 0x00 (NUL), 0x27 (single quote), 0xFF (high byte) — all hex, none literal.
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new byte[] { 0x00, 0x27, 0xFF })).IsEqualTo(@"CAST('\x00\x27\xFF' AS BLOB)");

        await Assert.That(DuckDBSqlLiteralEncoder.Encode(Array.Empty<byte>())).IsEqualTo("CAST('' AS BLOB)");
    }

    // =============================================================================================================
    // Lists — float vectors, string lists, numeric lists, empties.
    // =============================================================================================================

    [Test]
    public async Task Encode_FloatCollections_CastToFixedSizeArray() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new[] { 1f, 0f, 0f, 0f })).IsEqualTo("CAST([1, 0, 0, 0] AS FLOAT[4])");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new ReadOnlyMemory<float>(new[] { 1.5f, -2.5f }))).IsEqualTo("CAST([1.5, -2.5] AS FLOAT[2])");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new List<float> { 0.1f, 0.2f })).IsEqualTo("CAST([0.1, 0.2] AS FLOAT[2])");

        // A non-finite element is individually cast so it still parses inside the array.
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new[] { 1f, float.NaN })).IsEqualTo("CAST([1, CAST('NaN' AS FLOAT)] AS FLOAT[2])");
    }

    [Test]
    public async Task Encode_EmptyFloatCollection_IsVariableLengthList() {
        // A fixed-size FLOAT[0] is illegal in DuckDB, so an empty float collection degrades to a variable-length list.
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new List<float>())).IsEqualTo("CAST([] AS FLOAT[])");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(Array.Empty<float>())).IsEqualTo("CAST([] AS FLOAT[])");
    }

    [Test]
    public async Task Encode_StringLists_EncodeEachElement() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new List<string> { "a", "b" })).IsEqualTo("['a', 'b']");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new[] { "O'Brien" })).IsEqualTo("['O''Brien']");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new List<string>())).IsEqualTo("[]");

        // A null element inside a string list is NULL, not an empty string.
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new[] { "a", null })).IsEqualTo("['a', NULL]");
    }

    [Test]
    public async Task Encode_NumericLists_AreBareBracketedElements() {
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new[] { 1, 2, 3 })).IsEqualTo("[1, 2, 3]");
        await Assert.That(DuckDBSqlLiteralEncoder.Encode(new List<long> { -5L, 9223372036854775807L })).IsEqualTo("[-5, 9223372036854775807]");
    }

    [Test]
    public async Task Encode_UnsupportedType_Throws() => await Assert.That(() => DuckDBSqlLiteralEncoder.Encode(Guid.NewGuid())).Throws<NotSupportedException>();

    // =============================================================================================================
    // EncodeInto — placeholder substitution respecting quoted regions.
    // =============================================================================================================

    [Test]
    public async Task EncodeInto_ReplacesPlaceholdersInOrder() {
        await Assert.That(DuckDBSqlLiteralEncoder.EncodeInto("SELECT ?", [42])).IsEqualTo("SELECT 42");
        await Assert.That(DuckDBSqlLiteralEncoder.EncodeInto("SELECT ?, ?", ["a", 2])).IsEqualTo("SELECT 'a', 2");
        await Assert.That(DuckDBSqlLiteralEncoder.EncodeInto("SELECT ?", [null])).IsEqualTo("SELECT NULL");
    }

    [Test]
    public async Task EncodeInto_DoesNotSubstituteInsideStringLiteral() {
        // The bare '?' inside the literal is data; only the trailing '?' is a placeholder.
        await Assert.That(DuckDBSqlLiteralEncoder.EncodeInto("SELECT '?', ?", [7])).IsEqualTo("SELECT '?', 7");

        // A doubled quote inside the literal keeps it open, so the '?' stays data.
        await Assert.That(DuckDBSqlLiteralEncoder.EncodeInto("SELECT 'O''Brien?', ?", [7])).IsEqualTo("SELECT 'O''Brien?', 7");
    }

    [Test]
    public async Task EncodeInto_DoesNotSubstituteInsideQuotedIdentifier() =>
        await Assert.That(DuckDBSqlLiteralEncoder.EncodeInto("SELECT \"a?b\", ?", [7])).IsEqualTo("SELECT \"a?b\", 7");

    [Test]
    public async Task EncodeInto_CountMismatch_Throws() {
        // Too few values.
        await Assert.That(() => DuckDBSqlLiteralEncoder.EncodeInto("SELECT ?, ?", [1])).Throws<ArgumentException>();

        // Too many values.
        await Assert.That(() => DuckDBSqlLiteralEncoder.EncodeInto("SELECT ?", [1, 2])).Throws<ArgumentException>();
    }

    [Test]
    public async Task EncodeInto_PropagatesEncodingErrors() =>
        // A NUL in a substituted value fails the whole splice rather than corrupting the statement.
        await Assert.That(() => DuckDBSqlLiteralEncoder.EncodeInto("SELECT ?", ["a\0b"])).Throws<ArgumentException>();

    // =============================================================================================================
    // Live round-trip proofs — the DB itself is the oracle.
    // =============================================================================================================

    [Test]
    [LanceRequired]
    [Arguments("plain")]
    [Arguments("O'Brien")]
    [Arguments("'; DROP TABLE x; --")]
    [Arguments("line1\nline2\ttab")]
    [Arguments("🦆 mixed مرحبا")]
    [Arguments("")]
    public async Task RoundTrip_String(string value) {
        var got = await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(value));
        await Assert.That(got).IsEqualTo(value);
    }

    [Test]
    [LanceRequired]
    public async Task RoundTrip_ScalarsAndTemporal() {
        await Assert.That((bool)(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(true)))!).IsTrue();
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(-12345))).IsEqualTo(-12345);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(long.MaxValue))).IsEqualTo(long.MaxValue);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(0.1f))).IsEqualTo(0.1f);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(-2.5f))).IsEqualTo(-2.5f);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(float.MaxValue))).IsEqualTo(float.MaxValue);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(0.1d))).IsEqualTo(0.1d);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(123.456m))).IsEqualTo(123.456m);

        var dt = new DateTime(
            2024, 1, 2,
            3, 4, 5).AddTicks(1_230_000);

        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(dt))).IsEqualTo(dt);

        var dto = new DateTimeOffset(
            2024, 1, 2,
            3, 4, 5,
            TimeSpan.FromMinutes(330));

        // TIMESTAMPTZ read back under a UTC session equals the UTC instant (DateTime.Equals ignores Kind).
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(dto))).IsEqualTo(dto.UtcDateTime);
    }

    [Test]
    [LanceRequired]
    public async Task RoundTrip_NonFiniteFloats() {
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(float.PositiveInfinity))).IsEqualTo(float.PositiveInfinity);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(float.NegativeInfinity))).IsEqualTo(float.NegativeInfinity);
        await Assert.That(await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(double.NaN))).IsEqualTo(double.NaN);
    }

    [Test]
    [LanceRequired]
    public async Task RoundTrip_Blob() {
        byte[] payload = [0x00, 0x27, 0xFF, 0x41, 0x00, 0xAB];

        // RoundTripAsync materializes the BLOB (returned as a native-backed Stream) into a byte[] before the
        // connection is disposed, so the 0x00 / 0x27 / 0xFF bytes are compared byte-for-byte.
        var got = await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(payload));
        await Assert.That((byte[])got!).IsEquivalentTo(payload);
    }

    [Test]
    [LanceRequired]
    public async Task RoundTrip_FloatVector() {
        var vector = new[] { 1f, 0f, -0.5f, 3.25f };
        var got    = await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(vector));

        // FLOAT[n] comes back as List<float> (type fidelity fact).
        await Assert.That(((List<float>)got!).ToArray()).IsEquivalentTo(vector);
    }

    [Test]
    [LanceRequired]
    public async Task RoundTrip_StringList() {
        var tags = new List<string> {
            "alpha",
            "O'Brien",
            "🦆"
        };

        var got = await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(tags));

        await Assert.That((List<string>)got!).IsEquivalentTo(tags);
    }

    [Test]
    [LanceRequired]
    public async Task RoundTrip_EmptyFloatList_IsEmpty() {
        var got = await RoundTripAsync(DuckDBSqlLiteralEncoder.Encode(new List<float>()));
        await Assert.That(((List<float>)got!).Count).IsEqualTo(0);
    }

    /// <summary>
    /// Evaluates <c>SELECT {literal} AS v</c> on a fresh in-memory DuckDB connection (session TimeZone pinned to UTC
    /// so TIMESTAMPTZ reads back as the UTC instant) and returns the single cell (<c>DBNull</c> mapped to null).
    /// </summary>
    static async Task<object?> RoundTripAsync(string literal) {
        await using var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var setup = connection.CreateCommand()) {
            setup.CommandText = "SET TimeZone='UTC'";
            await setup.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {literal} AS v";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        var value = reader.GetValue(0);

        if (value is DBNull)
            return null;

        // A BLOB comes back as a native-backed Stream whose memory is freed when the connection/reader below are
        // disposed; copy it into a managed byte[] while it is still alive. (List<float>/List<string> are already
        // materialized as managed collections and are safe to return directly.)
        if (value is Stream stream) {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        return value;
    }
}