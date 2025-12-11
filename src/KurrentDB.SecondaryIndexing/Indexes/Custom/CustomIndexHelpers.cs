// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public static class CustomIndexHelpers {
	public static string GetQueryStreamName(string indexName, string? field = null) {
		field = field is null ? string.Empty : $"{CustomIndexConstants.FieldDelimiter}{field}";
		return $"{CustomIndexConstants.StreamPrefix}{indexName}{field}";
	}

	// For the SubscriptionService to drop all subscriptions to this custom index or any of its fields
	public static Regex GetStreamNameRegex(string indexName) {
		var streamName = GetQueryStreamName(indexName);
		var pattern = $"^{Regex.Escape(streamName)}({CustomIndexConstants.FieldDelimiter}.*)?$";
		return new Regex(pattern, RegexOptions.Compiled);
	}

	// Parses the custom index name [and field] out of the index stream that is being read
	// $idx-custom-<indexname>[:field]
	public static void ParseQueryStreamName(string streamName, out string indexName, out string? field) {
		if (!TryParseQueryStreamName(streamName, out indexName, out field))
			throw new Exception($"Unexpected error: could not parse custom index stream name {streamName}");
	}

	// Parses the custom index name [and field] out of the index stream that is being read
	// $idx-custom-<indexname>[:field]
	public static bool TryParseQueryStreamName(string streamName, out string indexName, out string? field) {
		if (!streamName.StartsWith(CustomIndexConstants.StreamPrefix)) {
			indexName = "";
			field = null;
			return false;
		}
		var delimiterIdx = streamName.IndexOf(CustomIndexConstants.FieldDelimiter, CustomIndexConstants.StreamPrefix.Length);
		if (delimiterIdx < 0) {
			indexName = streamName[CustomIndexConstants.StreamPrefix.Length..];
			field = null;
		} else {
			indexName = streamName[CustomIndexConstants.StreamPrefix.Length..delimiterIdx];
			field = streamName[(delimiterIdx + 1)..];
		}
		return true;
	}

	// Gets the management stream name for a particular custom index
	// "my-index" -> "$CustomIndex-my-index"
	public static string GetManagementStreamName(string indexName) {
		return $"{CustomIndexConstants.Category}-{indexName}";
	}

	// Parses the custom index name out of the management stream for that custom index
	// $CustomIndex-<indexname>
	public static string ParseManagementStreamName(string streamName) {
		Debug.Assert(streamName.StartsWith(CustomIndexConstants.Category));
		return streamName[(streamName.IndexOf('-') + 1)..];
	}
}
