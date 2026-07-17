// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Kontext.Mcp.Model;

namespace Kurrent.Kontext.Edges.Mcp.Model;

/// <summary>
/// Parses the canonical encoded tag form ("scope:value", or bare "value") and owns the sanitization
/// rule that makes tags canonical. <see cref="Tag"/> itself is a plain record and enforces nothing —
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
	public static string Sanitize(ReadOnlySpan<char> raw) {
		var builder  = new StringBuilder(raw.Length);
		var boundary = false; // a '-' is pending: we saw non-alphanumerics after real content

		foreach (var ch in raw) {
			if (char.IsLetterOrDigit(ch)) {
				if (boundary && builder.Length > 0)
					builder.Append('-');

				boundary = false;
				builder.Append(char.ToLowerInvariant(ch));
			} else {
				boundary = true;
			}
		}

		return builder.ToString();
	}
}
