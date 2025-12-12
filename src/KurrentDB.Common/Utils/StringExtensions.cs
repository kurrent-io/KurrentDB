// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KurrentDB.Common.Utils;

public static class StringExtensions {
	public static bool IsEmptyString(this string s) {
		return string.IsNullOrEmpty(s);
	}

	public static bool IsNotEmptyString(this string s) {
		return !string.IsNullOrEmpty(s);
	}

	public static bool EqualsOrdinalIgnoreCase(this string a, string b) =>
		string.Compare(a, b, StringComparison.OrdinalIgnoreCase) == 0;

	public static bool EqualsOrdinalIgnoreCase(this IEnumerable<string> a, IEnumerable<string> b) =>
		a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);

	public static ReadOnlyMemory<byte> ToUtf8Bytes(this string s) {
		return Encoding.UTF8.GetBytes(s);
	}
}
