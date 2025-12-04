// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

using static Core.Services.SystemStreams;

public static class CustomIndex {
	public static string GetStreamName(string indexName, string? partition = null) {
		partition = partition is null ? string.Empty : $"{CustomIndexPartitionKeyDelimiter}{partition}";
		return $"{IndexStreamPrefix}{indexName}{partition}";
	}

	public static Regex GetStreamNameRegex(string indexName) {
		var streamName = GetStreamName(indexName);
		var pattern = $"^{Regex.Escape(streamName)}({CustomIndexPartitionKeyDelimiter}.*)?$";
		return new Regex(pattern, RegexOptions.Compiled);
	}

	public static void ParseStreamName(string streamName, out string indexName, out string? partition) {
		var delimiterIdx = streamName.IndexOf(CustomIndexPartitionKeyDelimiter, IndexStreamPrefix.Length);
		if (delimiterIdx < 0) {
			indexName = streamName[IndexStreamPrefix.Length..];
			partition = null;
		} else {
			indexName = streamName[IndexStreamPrefix.Length..delimiterIdx];
			partition = streamName[(delimiterIdx + 1)..];
		}
	}

}
