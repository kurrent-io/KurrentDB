// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Kurrent.Quack;

namespace KurrentDB.Components.Stats;

internal record struct CategoryName(string Category);

internal static class StatsSql {
	public struct GetAllCategories : IQuery<CategoryName> {
		public static ReadOnlySpan<byte> CommandText => "select distinct category from idx_all"u8;

		public static CategoryName Parse(ref DataChunk.Row row) => new(row.ReadString());
	}
}
