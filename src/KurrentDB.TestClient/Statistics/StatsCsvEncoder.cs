// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KurrentDB.TestClient.Statistics;

public static class StatsCsvEncoder {
	private const string Comma = ",";
	private const string CommaEscapeSymbol = ";";

	public static string GetHeader(Dictionary<string, object> stats) {
		return Join(stats.Keys).Prepend("Time");
	}

	public static string GetLine(Dictionary<string, object> stats) {
		return Join(stats.Values).PrependTime();
	}

	private static string Prepend(this string csvLine, string column) {
		return string.Format("{0}{1}{2}", column, Comma, csvLine);
	}

	private static string PrependTime(this string csvLine) {
		return csvLine.Prepend(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
	}

	private static string Join(IEnumerable<object> items) {
		var strValues = items.Select(TryGetInvariantString);
		var escapedValues = strValues.Select(str => str.Replace(Comma, CommaEscapeSymbol)); //extra safety

		return string.Join(Comma, escapedValues);
	}

	private static string TryGetInvariantString(object obj) {
		if (obj == null)
			return string.Empty;

		var convertible = obj as IConvertible;
		if (convertible != null)
			return convertible.ToString(CultureInfo.InvariantCulture);

		return obj.ToString();
	}
}
