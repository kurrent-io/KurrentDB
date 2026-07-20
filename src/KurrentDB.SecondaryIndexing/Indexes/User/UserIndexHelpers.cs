// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public static class UserIndexHelpers {
	public static string GetQueryStreamName(ReadOnlySpan<char> indexName) =>
		$"{UserIndexConstants.StreamPrefix}{indexName}";

	public static void ParseQueryStreamName(string streamName, out ReadOnlyMemory<char> indexName, out ReadOnlyMemory<char>? suffix) {
		if (!TryParseQueryStreamName(streamName, out indexName, out suffix))
			throw new Exception($"Unexpected error: could not parse user index stream name {streamName}");
	}

	public static bool TryParseQueryStreamName(string streamName, out ReadOnlyMemory<char> indexName, out ReadOnlyMemory<char>? suffix) {
		if (!streamName.StartsWith(UserIndexConstants.StreamPrefix)) {
			indexName = ReadOnlyMemory<char>.Empty;
			suffix = null;
			return false;
		}

		// index names never contain the delimiter, so the first ':' after the prefix separates name from constraints
		var delimiterIdx = streamName.IndexOf(UserIndexConstants.FieldDelimiter, UserIndexConstants.StreamPrefix.Length);
		var streamNameMem = streamName.AsMemory();
		if (delimiterIdx < 0) {
			indexName = streamNameMem[UserIndexConstants.StreamPrefix.Length..];
			suffix = null;
		} else {
			indexName = streamNameMem[UserIndexConstants.StreamPrefix.Length..delimiterIdx];
			suffix = streamNameMem[(delimiterIdx + 1)..];
		}

		return true;
	}

	public static string GetManagementStreamName(string indexName) =>
		$"{UserIndexConstants.Category}{indexName}";

	internal static bool TryParseConstraints(IReadOnlyList<IField> fields, ReadOnlyMemory<char>? suffix, out IReadOnlyList<FieldConstraint> constraints) {
		if (suffix is null) {
			constraints = [];
			return true;
		}

		var parsed = new List<FieldConstraint>();
		constraints = parsed;

		var span = suffix.Value.Span;

		// single-field index: the whole suffix is the field's legacy literal value (which may contain '='),
		// UNLESS it explicitly begins with "<field>="
		if (fields.Count == 1) {
			var field = fields[0];
			return IsNewForm(field, span)
				? TryParseKeyValuePairs(span, fields, parsed)
				: TryAddSingle(field, span, parsed);
		}

		// multi-field index: only the new form is valid.

		// an empty suffix ("$idx-user-<name>:") is rejected.
		if (span.IsEmpty)
			return false;

		return TryParseKeyValuePairs(span, fields, parsed);
	}

	// True when the suffix explicitly begins with "<field name>=", i.e. it is the new field=value form for this field.
	private static bool IsNewForm(IField field, ReadOnlySpan<char> suffix) {
		var name = field.Name;
		return suffix.Length > name.Length && suffix[name.Length] == '=' && suffix[..name.Length].SequenceEqual(name);
	}

	private static bool TryParseKeyValuePairs(ReadOnlySpan<char> suffix, IReadOnlyList<IField> fields, List<FieldConstraint> parsed) {
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var sb = new StringBuilder();

		ReadOnlySpan<char> slice = suffix;

		while (!slice.IsEmpty) {
			if (!TryParseKey(ref slice, fields, out var key))
				return false;

			sb.Clear();
			if (!TryParseValue(ref slice, sb, out var value))
				return false;

			if (!TryAdd(key!, value.AsMemory().Span, seen, parsed))
				return false;
		}

		return true;
	}

	private static bool TryParseKey(ref ReadOnlySpan<char> slice, IReadOnlyList<IField> fields, out IField? field) {
		field = null;

		var delimiter = slice.IndexOf('=');
		if (delimiter < 0)
			return false;

		var key = slice[..delimiter];
		slice = slice[(delimiter + 1)..];

		return TryFindField(fields, key, out field);
	}

	private static bool TryParseValue(ref ReadOnlySpan<char> slice, StringBuilder sb, out string value) {
		value = string.Empty;

		if (slice.IsEmpty)
			return false;

		if (slice[0] is '"') {
			slice = slice[1..];
			return TryParseQuotedValue(ref slice, sb, out value);
		}

		return TryParseUnquotedValue(ref slice, out value);
	}

	private static bool TryParseUnquotedValue(ref ReadOnlySpan<char> slice, out string value) {
		value = string.Empty;

		var delimiter = slice.IndexOf(';');

		if (delimiter < 0)
			delimiter = slice.Length; // tolerate a missing ; for the last key/value pair

		var raw = slice[..delimiter];
		if (raw.Contains('"'))
			return false; // a quote may only wrap the whole value

		value = raw.ToString();
		slice = delimiter < slice.Length ? slice[(delimiter + 1)..] : [];

		return value.Length > 0;
	}

	private static bool TryParseQuotedValue(ref ReadOnlySpan<char> slice, StringBuilder sb, out string value) {
		value = string.Empty;

		for (var i = 0; i < slice.Length; i++) {
			switch (slice[i]) {
				case '\\':
					if (++i >= slice.Length)
						return false;

					if (slice[i] != '"' && slice[i] != '\\')
						return false;

					sb.Append(slice[i]);
					break;
				case '"':
					// tolerate a missing ; for the last key/value pair
					if (i + 1 < slice.Length && slice[++i] != ';')
						return false;

					slice = slice[(i + 1)..];
					value = sb.ToString();

					return true;
				default:
					sb.Append(slice[i]);
					break;
			}
		}

		// ran off the end without a terminating quote (")
		return false;
	}

	private static bool TryAddSingle(IField field, ReadOnlySpan<char> value, List<FieldConstraint> parsed) =>
		TryAdd(field, value, [], parsed);

	private static bool TryAdd(IField field, ReadOnlySpan<char> value, HashSet<string> seen, List<FieldConstraint> parsed) {
		if (!seen.Add(field.Name))
			return false;

		try {
			parsed.Add(new(field, field.NormalizeValue(value)));
			return true;
		} catch {
			return false; // value does not parse to the field's type
		}
	}

	private static bool TryFindField(IReadOnlyList<IField> fields, ReadOnlySpan<char> key, [NotNullWhen(true)] out IField? field) {
		foreach (var candidate in fields) {
			if (key.SequenceEqual(candidate.Name)) {
				field = candidate;
				return true;
			}
		}

		field = null;
		return false;
	}
}
