// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

// Values mirror Contracts.MemoryType numerically — the mapper is a cast. 4 and 6 are the retired
// Procedure and Plan; their numbers stay unassigned so old serialized values cannot alias.
public enum MemoryType {
	Unspecified = 0,

	Observation = 1,

	Hearsay = 2,

	Fact = 3,

	UserProfile = 5,

	Summary = 7,

	Preference = 8,

	OpenQuestion = 9,
}
