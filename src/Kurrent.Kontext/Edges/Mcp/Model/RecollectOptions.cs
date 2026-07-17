// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

public enum RecollectSort {
	Unspecified = 0,

	RetainedAt = 1,

	LastAccessedAt = 2,

	Importance = 3,
}

public enum SortDirection {
	Unspecified = 0,

	Descending = 1,

	Ascending = 2,
}

public sealed class RecollectOptions {
	public IReadOnlyList<MemoryType> Types { get; set; } = [];

	public IReadOnlyList<Tag> Tags { get; set; } = [];

	public int Limit { get; set; }

	public RecollectSort Sort { get; set; }

	public SortDirection Direction { get; set; }
}
