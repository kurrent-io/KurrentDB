// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Memory.Mcp.Model;

// Values mirror Contracts.RetainOutcome numerically — the mapper is a cast, so renumbering the
// proto enum without renumbering this one remaps every outcome silently.
public enum RetainOutcome {
	Unspecified = 0,

	Created = 1,

	Noop = 2,
}
