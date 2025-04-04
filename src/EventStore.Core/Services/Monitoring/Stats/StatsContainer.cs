// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using JetBrains.Annotations;

namespace EventStore.Core.Services.Monitoring.Stats;

public class StatsContainer {
	private readonly Dictionary<string, object> _stats = new();

	private const string Separator = "-";
	private static readonly string[] SplitSeparator = [Separator];

	public void Add(IDictionary<string, object> statGroup) {
		Ensure.NotNull(statGroup);

		foreach (var stat in statGroup)
			_stats.Add(stat.Key, stat.Value);
	}

	public Dictionary<string, object> GetStats(bool useGrouping, bool useMetadata) {
		return useGrouping switch {
			true when useMetadata => GetGroupedStatsWithMetadata(),
			true when !useMetadata => GetGroupedStats(),
			false when useMetadata => GetRawStatsWithMetadata(),
			_ => GetRawStats()
		};
	}

	private Dictionary<string, object> GetGroupedStatsWithMetadata() {
		var grouped = Group(_stats);
		return grouped;
	}

	private Dictionary<string, object> GetGroupedStats() {
		var values = GetStatsValues(_stats);
		var grouped = Group(values);
		return grouped;
	}

	private Dictionary<string, object> GetRawStatsWithMetadata() {
		return new(_stats);
	}

	private Dictionary<string, object> GetRawStats() {
		var values = GetStatsValues(_stats);
		return values;
	}

	private static Dictionary<string, object> GetStatsValues(Dictionary<string, object> dictionary) {
		return dictionary.ToDictionary(
			kvp => kvp.Key,
			kvp => {
				var statInfo = kvp.Value as StatMetadata;
				return statInfo == null ? kvp.Value : statInfo.Value;
			});
	}

	public static Dictionary<string, object> Group(Dictionary<string, object> input) {
		Ensure.NotNull(input);

		if (input.IsEmpty())
			return input;

		var groupContainer = NewDictionary();
		var hasSubGroups = false;

		foreach (var entry in input) {
			var groups = entry.Key.Split(SplitSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (groups.Length < 2) {
				groupContainer.Add(entry.Key, entry.Value);
				continue;
			}

			hasSubGroups = true;

			string prefix = groups[0];
			string remaining = string.Join(Separator, groups.Skip(1).ToArray());

			if (!groupContainer.ContainsKey(prefix))
				groupContainer.Add(prefix, NewDictionary());

			((Dictionary<string, object>)groupContainer[prefix]).Add(remaining, entry.Value);
		}

		if (!hasSubGroups)
			return groupContainer;

		// we must first iterate through all dictionary and then aggregate it again
		var result = NewDictionary();

		foreach (var entry in groupContainer) {
			var subgroup = entry.Value as Dictionary<string, object>;
			result[entry.Key] = subgroup != null ? Group(subgroup) : entry.Value;
		}

		return result;
	}

	private static Dictionary<string, object> NewDictionary() => new(new CaseInsensitiveStringComparer());

	private class CaseInsensitiveStringComparer : IEqualityComparer<string> {
		public bool Equals(string x, string y) => string.Equals(x, y, StringComparison.InvariantCultureIgnoreCase);

		public int GetHashCode([CanBeNull] string obj) => obj != null ? obj.ToUpperInvariant().GetHashCode() : -1;
	}
}
