// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Services;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal static class CategoryIndex {
	public const string Prefix = $"{SystemStreams.IndexStreamPrefix}ce-";
	public static string Name(string categoryName) => $"{Prefix}{categoryName}";

	public static bool TryGetCategoryName(string streamName, [NotNullWhen(true)] out string? categoryName) {
		if (!IsCategoryIndexStream(Prefix)) {
			categoryName = null;
			return false;
		}

		categoryName = streamName[8..];
		return true;
	}

	public static bool IsCategoryIndexStream(string streamName) =>
		streamName.StartsWith(Prefix);
}
