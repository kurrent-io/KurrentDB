// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Services;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

static class CategoryIndex {
	public static string Name(string categoryName) => $"{SystemStreams.CategorySecondaryIndexPrefix}{categoryName}";

	public static bool TryParseCategoryName(string streamName, [NotNullWhen(true)] out string? categoryName) {
		if (!IsCategoryIndexStream(SystemStreams.CategorySecondaryIndexPrefix)) {
			categoryName = null;
			return false;
		}

		categoryName = streamName[8..];
		return true;
	}

	public static bool IsCategoryIndexStream(string streamName) => streamName.StartsWith(SystemStreams.CategorySecondaryIndexPrefix);
}
