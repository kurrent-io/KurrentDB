// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;

namespace Kurrent.Kontext.Core;

/// <summary>
/// An agent-agnostic filter label with an optional scope (namespace). Scoped tags let you filter on a typed
/// dimension (e.g. project:kurrentdb) without a contract change per dimension, and the same scope can repeat on
/// one memory (country:portugal + country:norway). Because <see cref="Scope"/> is a free string, new dimensions
/// need no contract change — which is why identity and context live here, not in dedicated fields.
/// <para>
/// Both <see cref="Value"/> and <see cref="Scope"/> are sanitized to lower kebab-case on creation, so tags are
/// canonical — "Project" and "project" collapse to the same tag. Round-trips to a compact "scope:value" (or
/// bare "value") string via <see cref="Encoded"/> / <see cref="Parse"/>.
/// </para>
/// </summary>
/// <remarks>
/// Well-known scopes (a convention, not a closed set): <c>user</c> (the isolation dimension — stamped and
/// enforced from the connection under auth), <c>session</c>, <c>project</c>, <c>workspace</c>. An empty scope is
/// a bare tag.
/// </remarks>
public readonly record struct Tag {
	// Private so the only ways to build a Tag are Create/Parse, which guarantee the lower-kebab invariant —
	// a positional record would let `new Tag("RAW")` (and `with`) smuggle in unsanitized values.
	Tag(string value, string scope) {
		Value = value;
		Scope = scope;
		// Precompute the canonical form once — Tag is immutable, so the encoded string never changes. A bare
		// tag aliases Value (no allocation); only a scoped tag allocates the "scope:value" string, here at creation.
		Encoded = string.IsNullOrEmpty(scope) ? value : $"{scope}:{value}";
	}
	
	/// <summary>The label — lower kebab-case.</summary>
	public string Value { get; }

	/// <summary>Namespace — lower kebab-case; "" ⇒ bare tag (never null via <see cref="Create"/> / <see cref="Parse"/>).</summary>
	public string Scope { get; }
	
	/// <summary>The canonical "scope:value" (or bare "value") string, computed once at creation.</summary>
	public string Encoded { get; }

	/// <summary>True when this tag carries a scope (is not bare).</summary>
	public bool HasScope => !string.IsNullOrEmpty(Scope);

	/// <summary>
	/// Create a tag, sanitizing <paramref name="value"/> and <paramref name="scope"/> to lower kebab-case. Omit
	/// <paramref name="scope"/> (or pass "") for a bare tag. Strings bind here via the implicit span conversion.
	/// </summary>
	public static Tag Create(ReadOnlySpan<char> value, ReadOnlySpan<char> scope = default) =>
		// An empty or all-punctuation scope sanitizes to "" — the single bare-tag representation, so two bare
		// tags with the same value always compare equal.
		new(Sanitize(value), Sanitize(scope));

	/// <summary>
	/// Decode a "scope:value" (or bare "value") string into a tag. Splits on the FIRST ':'; both parts are
	/// sanitized to lower kebab-case. Examples: "project:kurrentdb" ⇒ scope "project", value "kurrentdb";
	/// "research" ⇒ bare. Total — any input yields a (sanitized) tag, so it never throws. Strings bind here
	/// via the implicit span conversion.
	/// </summary>
	public static Tag Parse(ReadOnlySpan<char> encoded) {
		var separator = encoded.IndexOf(':');
		var value = separator < 0 ? encoded : encoded[(separator + 1)..];
		var scope = separator < 0 ? default : encoded[..separator];
		return new(Sanitize(value), Sanitize(scope));
	}

	/// <inheritdoc/>
	public override string ToString() => Encoded;

	// Lower kebab-case: lowercase each letter/digit, collapse every run of other characters to a single '-',
	// and never emit a leading or trailing '-'. Keeps Unicode letters (café stays café); it does not ASCII-fold.
	static string Sanitize(ReadOnlySpan<char> raw) {
		var builder = new StringBuilder(raw.Length);
		var boundary = false;  // a '-' is pending: we saw non-alphanumerics after real content
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
