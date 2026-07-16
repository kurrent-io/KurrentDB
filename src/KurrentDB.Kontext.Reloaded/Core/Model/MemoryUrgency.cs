// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// How time-pressing a memory's content is, tagged by the enrichment pipeline — NOT agent-set.
/// </summary>
public enum MemoryUrgency {
	/// <summary>Not yet classified.</summary>
	Unspecified = 0,

	/// <summary>No time pressure — background knowledge.</summary>
	Low = 1,

	/// <summary>Worth acting on before long.</summary>
	Medium = 2,

	/// <summary>Needs attention now (blocker, incident, deadline).</summary>
	High = 3,
}
