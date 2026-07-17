// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

public sealed class ReflectResult {
	public string QueryId { get; set; } = "";

	public IReadOnlyList<string> SynthesizedMemoryIds { get; set; } = [];

	public IReadOnlyList<string> SupersededMemoryIds { get; set; } = [];

	public IReadOnlyList<string> RetractedMemoryIds { get; set; } = [];
}
