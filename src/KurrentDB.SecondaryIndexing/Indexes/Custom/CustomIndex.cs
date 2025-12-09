// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

using static Core.Services.SystemStreams;

public static class CustomIndex {
	public static string GetStreamName(string indexName, string? field = null) {
		field = field is null ? string.Empty : $"{CustomIndexFieldDelimiter}{field}";
		return $"{IndexStreamPrefix}{indexName}{field}";
	}

	// For the SubscriptionService to drop all subscriptions to this custom index or any of its fields
	public static Regex GetStreamNameRegex(string indexName) {
		var streamName = GetStreamName(indexName);
		var pattern = $"^{Regex.Escape(streamName)}({CustomIndexFieldDelimiter}.*)?$";
		return new Regex(pattern, RegexOptions.Compiled);
	}

	public static void ParseStreamName(string streamName, out string indexName, out string? field) {
		var delimiterIdx = streamName.IndexOf(CustomIndexFieldDelimiter, IndexStreamPrefix.Length);
		if (delimiterIdx < 0) {
			indexName = streamName[IndexStreamPrefix.Length..];
			field = null;
		} else {
			indexName = streamName[IndexStreamPrefix.Length..delimiterIdx];
			field = streamName[(delimiterIdx + 1)..];
		}
	}

}
