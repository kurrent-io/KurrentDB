// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Parses the canonical encoded tag form ("scope:value", or bare "value") and owns the sanitization
/// rule that makes tags canonical. Tag records themselves are plain data and enforce nothing —
/// callers that need canonical parts go through here.
/// </summary>
public static class TagParser {
    public static (string Value, string Scope) Parse(ReadOnlySpan<char> encoded) {
        var separator = encoded.IndexOf(':');
        var value     = separator < 0 ? encoded : encoded[(separator + 1)..];
        var scope     = separator < 0 ? default : encoded[..separator];
        // An empty or all-punctuation scope sanitizes to "" — the single bare-tag representation.
        return (Sanitize(value), Sanitize(scope));
    }

    // Lower kebab-case: lowercase each letter/digit, collapse every run of other characters to a single '-',
    // and never emit a leading or trailing '-'. Keeps Unicode letters (café stays café); it does not ASCII-fold.
    //
    // One exception: a run that contains '/' collapses to '--' instead. That is the encoding for a repo slug,
    // which must survive as one tag value ("kurrent/kurrentdb" -> "kurrent--kurrentdb"). A run that is already
    // exactly "--" is preserved for the same reason, which makes the whole function idempotent — sanitizing an
    // encoded slug a second time must not degrade it to a single dash. Any longer run of dashes is ordinary
    // punctuation and still collapses to one.
    //
    // Matches: ^[\p{L}\p{N}]+(-{1,2}[\p{L}\p{N}]+)*$
    public static string Sanitize(ReadOnlySpan<char> raw) {
        var builder  = new StringBuilder(raw.Length);
        var boundary = false; // a separator is pending: we saw non-alphanumerics after real content
        var runLength = 0;    // characters in the pending run
        var runSlashes = 0;   // '/' seen in it
        var runDashes = 0;    // '-' seen in it

        foreach (var ch in raw) {
            if (char.IsLetterOrDigit(ch)) {
                if (boundary && builder.Length > 0)
                    builder.Append(IsSlugSeparator(runLength, runSlashes, runDashes) ? "--" : "-");

                boundary   = false;
                runLength  = 0;
                runSlashes = 0;
                runDashes  = 0;

                builder.Append(char.ToLowerInvariant(ch));
            } else {
                boundary = true;
                runLength++;

                if (ch == '/')
                    runSlashes++;
                else if (ch == '-')
                    runDashes++;
            }
        }

        return builder.ToString();

        static bool IsSlugSeparator(int length, int slashes, int dashes) => slashes > 0 || (length == 2 && dashes == 2);
    }
}