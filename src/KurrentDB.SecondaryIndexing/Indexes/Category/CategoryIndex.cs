// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using static KurrentDB.Core.Services.SystemStreams;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal static class CategoryIndex {
	private static readonly int PrefixLength = CategorySecondaryIndexPrefix.Length;

	public static string Name(string categoryName) => $"{CategorySecondaryIndexPrefix}{categoryName}";

	public static bool TryParseCategoryName(string indexName, [NotNullWhen(true)] out string? categoryName) {
		if (!IsCategoryIndex(indexName)) {
			categoryName = null;
			return false;
		}

		categoryName = indexName[PrefixLength..];
		return true;
	}

	public static bool IsCategoryIndex(string indexName) => indexName.StartsWith(CategorySecondaryIndexPrefix);
}
