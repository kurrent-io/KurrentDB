// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public static class UserIndexHelpers {
	public static string GetQueryStreamName(string indexName, string? field = null) {
		field = field is null ? string.Empty : $"{UserIndexConstants.FieldDelimiter}{field}";
		return $"{UserIndexConstants.StreamPrefix}{indexName}{field}";
	}

	// For the SubscriptionService to drop all subscriptions to this user index or any of its fields
	public static Regex GetStreamNameRegex(string indexName) {
		var streamName = GetQueryStreamName(indexName);
		var pattern = $"^{Regex.Escape(streamName)}({UserIndexConstants.FieldDelimiter}.*)?$";
		return new Regex(pattern, RegexOptions.Compiled);
	}

	// Parses the user index name [and field] out of the index stream that is being read
	// $idx-user-<indexname>[:field]
	public static void ParseQueryStreamName(string streamName, out string indexName, out string? field) {
		if (!TryParseQueryStreamName(streamName, out indexName, out field))
			throw new Exception($"Unexpected error: could not parse user index stream name {streamName}");
	}

	// Parses the user index name [and field] out of the index stream that is being read
	// $idx-user-<indexname>[:field]
	public static bool TryParseQueryStreamName(string streamName, out string indexName, out string? field) {
		if (!streamName.StartsWith(UserIndexConstants.StreamPrefix)) {
			indexName = "";
			field = null;
			return false;
		}
		var delimiterIdx = streamName.IndexOf(UserIndexConstants.FieldDelimiter, UserIndexConstants.StreamPrefix.Length);
		if (delimiterIdx < 0) {
			indexName = streamName[UserIndexConstants.StreamPrefix.Length..];
			field = null;
		} else {
			indexName = streamName[UserIndexConstants.StreamPrefix.Length..delimiterIdx];
			field = streamName[(delimiterIdx + 1)..];
		}
		return true;
	}

	// Gets the management stream name for a particular user index
	// "my-index" -> "$UserIndex-my-index"
	public static string GetManagementStreamName(string indexName) {
		return $"{UserIndexConstants.Category}-{indexName}";
	}
}
