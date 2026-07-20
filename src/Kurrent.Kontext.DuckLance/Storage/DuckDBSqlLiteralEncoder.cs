using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Converts CLR values into DuckDB SQL <em>literals</em>, so a value can be embedded directly in a SQL string
/// with no parameter binding — the security-critical companion to <see cref="DuckDBQuackConnectionManager"/>.
/// </summary>
static class DuckDBSqlLiteralEncoder {
    // Why this type exists: the quack beta protocol exposes NO parameter channel for the remote SQL it
    // tunnels (quack_query rejects any '?' placeholder inside the remote statement with "Expected N
    // parameters, but none were supplied"), so every value that reaches the server must already be a
    // complete literal. With no engine-side binding to fall back on, the encoder is written to an
    // adversarial standard: it fails closed (throws) rather than emit anything ambiguous. Internal and
    // experimental — it exists only to support the experimental quack connection manager and carries the
    // same beta/breaking-changes caveats.
    //
    // Encoding is type-directed and SELF-TYPED: every literal carries its own type (CAST('NaN' AS FLOAT),
    // CAST([...] AS FLOAT[4]), TIMESTAMPTZ '...+00:00'), so a plain SELECT {Encode(v)} reconstructs v on
    // its own. When EncodeInto later splices such a literal into a composer statement whose placeholder is
    // already wrapped in a cast (e.g. CAST(? AS FLOAT[4])), the resulting redundant double cast is harmless.
    //
    // The per-method comments below record DuckDB literal behaviors live-validated against DuckDB v1.5.x.

    /// <summary>Encodes one CLR value as a self-typed DuckDB SQL literal (<see langword="null"/>/<see cref="DBNull"/> encode to <c>NULL</c>).</summary>
    public static string Encode(object? value) {
        switch (value) {
            case null:
            case DBNull:
                return "NULL";

            case string s: return EncodeString(s);

            case bool b: return b ? "TRUE" : "FALSE";

            // Integral types: bare invariant digits (no thousands separators, no cast needed).
            case sbyte or byte or short or ushort or int or uint or long or ulong: return Convert.ToString(value, CultureInfo.InvariantCulture)!;

            case float f: return $"CAST('{FloatToken(f)}' AS FLOAT)";

            case double d: return $"CAST('{DoubleToken(d)}' AS DOUBLE)";

            case decimal m:
                // DECIMAL(38,18) is DuckDB's max precision with room for 18 fractional digits; it therefore trades
                // integer range (20 integer digits) for scale — values outside that range are rejected by the engine.
                return $"CAST('{m.ToString(CultureInfo.InvariantCulture)}' AS DECIMAL(38,18))";

            case DateTime dt: return $"TIMESTAMP '{dt.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}'";

            case DateTimeOffset dto:
                // Normalize to UTC and stamp an explicit +00:00 so the instant is unambiguous regardless of the
                // server session's TimeZone setting.
                return $"TIMESTAMPTZ '{dto.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}+00:00'";

            case byte[] bytes: return EncodeBlob(bytes);

            case ReadOnlyMemory<float> rom: return EncodeFloatArray(rom.Span);

            case float[] fa: return EncodeFloatArray(fa);

            case List<float> fl: return EncodeFloatArray(CollectionsMarshal.AsSpan(fl));

            // string[] and List<string> (and any other IEnumerable<string>): a bracketed list of encoded strings.
            case IEnumerable<string> strings: return EncodeStringList(strings);

            // Any remaining sequence (int[], List<int>, List<long>, List<double>, …): element-wise self-typed
            // encoding inside a bracketed list. Placed last so the string/byte[]/float-array cases above win first.
            case IEnumerable sequence: return EncodeSequence(sequence);

            default: throw new NotSupportedException($"{nameof(DuckDBSqlLiteralEncoder)} cannot encode a value of type '{value.GetType()}'.");
        }

        // Encodes a byte array as a fully hex-escaped BLOB literal, or CAST('' AS BLOB) when empty.
        static string EncodeBlob(byte[] bytes) {
            if (bytes.Length == 0)
                return "CAST('' AS BLOB)";

            // EVERY byte is written as \xNN: escaping all bytes (not just the awkward ones) sidesteps the NUL,
            // quote (0x27) and high-byte hazards uniformly.
            var builder = new StringBuilder(bytes.Length * 4 + 16);
            builder.Append("CAST('");

            foreach (var b in bytes)
                builder.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));

            builder.Append("' AS BLOB)");
            return builder.ToString();
        }

        // Encodes a float sequence as CAST([e0, e1, …] AS FLOAT[n]). A fixed-size array requires size >= 1 —
        // FLOAT[0] is illegal in DuckDB — so an empty collection is emitted as the variable-length LIST
        // CAST([] AS FLOAT[]) instead. Finite elements are bare round-trippable "R" tokens; non-finite ones
        // are individually cast (e.g. CAST('NaN' AS FLOAT)).
        static string EncodeFloatArray(ReadOnlySpan<float> values) {
            if (values.Length == 0)
                return "CAST([] AS FLOAT[])";

            var builder = new StringBuilder(values.Length * 12 + 24);
            builder.Append("CAST([");

            for (var i = 0; i < values.Length; i++) {
                if (i > 0)
                    builder.Append(", ");

                var f = values[i];
                builder.Append(float.IsFinite(f) ? f.ToString("R", CultureInfo.InvariantCulture) : $"CAST('{FloatToken(f)}' AS FLOAT)");
            }

            builder.Append("] AS FLOAT[").Append(values.Length.ToString(CultureInfo.InvariantCulture)).Append("])");
            return builder.ToString();
        }

        // Encodes a sequence of (possibly null) strings as a bracketed list of encoded string literals.
        static string EncodeStringList(IEnumerable<string> values) =>
            $"[{string.Join(", ", values.Select(item => item is null ? "NULL" : EncodeString(item)))}]";

        // Encodes an arbitrary sequence as a bracketed list, encoding each element with Encode.
        static string EncodeSequence(IEnumerable values) =>
            $"[{string.Join(", ", values.Cast<object?>().Select(Encode))}]";

        // The DuckDB literal body for a double: the round-trippable "R" token, or a non-finite word.
        static string DoubleToken(double value) =>
            value switch {
                double.NaN              => "NaN",
                double.PositiveInfinity => "Infinity",
                double.NegativeInfinity => "-Infinity",
                _                       => value.ToString("R", CultureInfo.InvariantCulture)
            };
    }

    /// <summary>
    /// Substitutes each positional <c>?</c> placeholder outside a quoted region with the corresponding
    /// encoded value from <c>values</c>, in order.
    /// </summary>
    public static string EncodeInto(string sqlWithPlaceholders, IReadOnlyList<object?> values) {
        // Reuses the existing '?'-shaped composer SQL unchanged, filling in the parameter channel the quack
        // protocol lacks. A '?' inside a single-quoted string literal or a double-quoted identifier
        // (including their doubled-quote escapes) is data, not a parameter, and is copied verbatim. A
        // placeholder/value count mismatch throws.
        var builder    = new StringBuilder(sqlWithPlaceholders.Length + values.Count * 8);
        var valueIndex = 0;
        var i          = 0;

        while (i < sqlWithPlaceholders.Length) {
            var c = sqlWithPlaceholders[i];

            if (c is '\'' or '"') {
                // Copy a quoted region verbatim; a doubled quote inside it is an escaped quote, not the terminator.
                CopyQuotedRegion(
                    sqlWithPlaceholders, builder, ref i,
                    c);

                continue;
            }

            if (c == '?') {
                if (valueIndex >= values.Count) {
                    throw new ArgumentException(
                        $"The SQL has more '?' placeholders than the {values.Count} value(s) supplied.",
                        nameof(sqlWithPlaceholders));
                }

                builder.Append(Encode(values[valueIndex]));
                valueIndex++;
                i++;
                continue;
            }

            builder.Append(c);
            i++;
        }

        if (valueIndex != values.Count) {
            throw new ArgumentException(
                $"The SQL has {valueIndex} substitutable '?' placeholder(s) but {values.Count} value(s) were supplied.",
                nameof(values));
        }

        return builder.ToString();

        // Copies the quoted region opened at sql[i] verbatim, advancing i past the closing quote. A doubled
        // quote is the SQL-standard escaped quote and keeps the region open. An unterminated region
        // (malformed template) is copied to the end and the loop ends naturally — no '?' inside it is ever
        // substituted, which is the safe outcome.
        static void CopyQuotedRegion(string sql, StringBuilder builder, ref int i, char quote) {
            builder.Append(quote);
            i++;

            while (i < sql.Length) {
                var d = sql[i];

                if (d == quote) {
                    if (i + 1 < sql.Length && sql[i + 1] == quote) {
                        builder.Append(quote).Append(quote);
                        i += 2;
                        continue;
                    }

                    builder.Append(quote);
                    i++;
                    return;
                }

                builder.Append(d);
                i++;
            }
        }
    }

    /// <summary>Encodes a string as a single-quoted literal, doubling embedded single quotes.</summary>
    static string EncodeString(string value) {
        // Live-validated: DuckDB's parser treats a raw NUL (\0) inside a single-quoted literal as a string
        // terminator ("unterminated quoted string"), and no escape exists for it in a text literal — so a
        // string containing NUL is rejected rather than silently truncated.
        if (value.IndexOf('\0') >= 0) {
            throw new ArgumentException(
                "Cannot encode a string containing a NUL ('\\0') character: DuckDB treats NUL as a string-literal "
              + "terminator, which would truncate or break the SQL. Reject or sanitize the value upstream instead.",
                nameof(value));
        }

        return string.Concat("'", value.Replace("'", "''", StringComparison.Ordinal), "'");
    }

    /// <summary>Returns the DuckDB literal body for a float: the round-trippable <c>"R"</c> token, or a non-finite word.</summary>
    static string FloatToken(float value) =>
        value switch {
            float.NaN              => "NaN",
            float.PositiveInfinity => "Infinity",
            float.NegativeInfinity => "-Infinity",
            _                      => value.ToString("R", CultureInfo.InvariantCulture)
        };
}