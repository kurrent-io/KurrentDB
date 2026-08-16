// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.Json;

namespace Kurrent.Kontext.Embeddings.Normalization;

/// <summary>
/// Renders a UTF-8 JSON payload as the embedding-friendly text sentence models score best on:
/// one <c>key: value</c> pair per line, pairs closed by a comma, keys split to lowercase words,
/// values unquoted. Anything that is not parseable JSON passes through unchanged — the
/// normalizer shapes content, it never destroys it.
/// <para>
/// The rendering rules are measurement-backed against the shipped model: the comma is a real
/// relevance signal, quote marks measurably hurt, casing and the whitespace after the comma
/// are neutral. Booleans and nulls are skipped as stop-word noise.
/// </para>
/// </summary>
public sealed class JsonNormalizer : IUtf8Normalizer {
    // Above System.Text.Json's default of 64 — deeply nested agent-session payloads are legitimate.
    static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = 128 };

    /// <summary>The shared instance — the normalizer is stateless.</summary>
    public static JsonNormalizer Instance { get; } = new();

    public string Normalize(ReadOnlySpan<byte> utf8) {
        // Cheap gate before parsing: JSON worth flattening starts as an object or array.
        var start = 0;
        while (start < utf8.Length && utf8[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            start++;

        if (start == utf8.Length || utf8[start] is not ((byte)'{' or (byte)'['))
            return Encoding.UTF8.GetString(utf8).Trim();

        try {
            var reader = new Utf8JsonReader(utf8, ReaderOptions);
            var pairs  = WalkPairs(ref reader);

            return pairs.Count > 0 ? string.Join(",\n", pairs) : Encoding.UTF8.GetString(utf8).Trim();
        } catch (JsonException) {
            return Encoding.UTF8.GetString(utf8).Trim();
        }
    }

    static List<string> WalkPairs(ref Utf8JsonReader reader) {
        var pairs   = new List<string>();
        var scratch = new StringBuilder();

        // The pending key: set by PropertyName, consumed by the next value or container.
        string? pendingKey = null;

        // One frame per open array: the key its direct scalar elements accumulate under.
        var arrays = new Stack<(string? Key, List<string> Scalars)>();

        while (reader.Read()) {
            switch (reader.TokenType) {
                case JsonTokenType.PropertyName:
                    pendingKey = SplitName(reader.GetString()!, scratch);
                    break;

                case JsonTokenType.StartObject:
                    // The object consumed its key; its children carry their own.
                    pendingKey = null;
                    break;

                case JsonTokenType.StartArray:
                    // Scalar elements collapse into ONE pair under the array's key ("tags: a b c");
                    // a nested array inherits the enclosing array's key.
                    arrays.Push((pendingKey ?? (arrays.TryPeek(out var outer) ? outer.Key : null), []));
                    pendingKey = null;
                    break;

                case JsonTokenType.EndArray: {
                    var (key, scalars) = arrays.Pop();

                    if (scalars.Count > 0)
                        Emit(pairs, key, string.Join(' ', scalars));

                    break;
                }

                case JsonTokenType.String:
                case JsonTokenType.Number: {
                    var value = reader.TokenType == JsonTokenType.String
                        ? reader.GetString()
                        : Encoding.UTF8.GetString(reader.ValueSpan);

                    if (!string.IsNullOrWhiteSpace(value)) {
                        value = value.Trim();

                        if (pendingKey is not null)
                            Emit(pairs, pendingKey, value);
                        else if (arrays.TryPeek(out var array))
                            array.Scalars.Add(value);
                        else
                            Emit(pairs, key: null, value);
                    }

                    pendingKey = null;
                    break;
                }

                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    // Skipped as stop-word noise — but the value still consumed its key.
                    pendingKey = null;
                    break;
            }
        }

        return pairs;
    }

    static void Emit(List<string> pairs, string? key, string value) =>
        pairs.Add(key is null ? value : $"{key}: {value}");

    // "toolName" / "tool_name" / "tool-name" -> "tool name": lowercase, word-split at case and
    // separator boundaries, so keys read as prose instead of identifiers.
    static string SplitName(string name, StringBuilder scratch) {
        scratch.Clear();

        for (var i = 0; i < name.Length; i++) {
            var c = name[i];

            if (c is '_' or '-')
                scratch.Append(' ');
            else if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                scratch.Append(' ').Append(char.ToLowerInvariant(c));
            else
                scratch.Append(char.ToLowerInvariant(c));
        }

        return scratch.ToString().Trim();
    }
}
