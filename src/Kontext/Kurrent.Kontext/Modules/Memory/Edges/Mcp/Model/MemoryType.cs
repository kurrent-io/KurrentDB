// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Memory.Mcp.Model;

// Values mirror Contracts.MemoryType numerically — the mapper is a cast, so renumbering the proto
// enum without renumbering this one remaps every type silently.
public enum MemoryType {
	Unspecified = 0,

	Observation = 1,

	Hearsay = 2,

	Fact = 3,

	UserProfile = 4,

	Summary = 5,

	Preference = 6,

	OpenQuestion = 7,
}
