// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;

namespace KurrentDB.TestClient;

/// <summary>
///	Helps making it easier to specify large values without making a mistake.
/// With this we can write: WRFLGRPC 10 5M 500k 100 1 perftest
/// instead of: WRFLGRPC 10 5000000 500000 100 1 perftest
/// </summary>
internal static class MetricPrefixValue {
	private static readonly Dictionary<char, long> _metricPrefixes = new () {
		{'k', 1_000},
		{'m', 1_000_000},
		{'g', 1_000_000_000},
	};

	public static int ParseInt(string value) => int.Parse(Expand(value));

	public static long ParseLong(string value) => long.Parse(Expand(value));

	private static string Expand(string value) {
		value = value.ToLower();

		if (!_metricPrefixes.Keys.Any(k => value.EndsWith(k))) {
			return value;
		}

		var metricPrefix = value[^1];
		var numericPart = value[..^1];
		var v = double.Parse(numericPart) * _metricPrefixes[metricPrefix];
		return ((long)v).ToString();
	}
}
