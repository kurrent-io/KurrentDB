// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The agent's salience rating, set at retain. An <em>additive</em> term in ranking, not a decay control:
/// higher importance keeps a memory's score up as recency fades, so it stays retrievable longer — it does
/// NOT change the decay rate itself. A coarse bucket by design — an enum set consistently beats a float
/// never calibrated the same way twice.
/// </summary>
public enum MemoryImportance {
	/// <summary>Not set — treated as <see cref="Normal"/>.</summary>
	Unspecified = 0,

	/// <summary>Incidental — fine to let it fade.</summary>
	Low = 1,

	/// <summary>The default.</summary>
	Normal = 2,

	/// <summary>Decisions, fixes, preferences worth keeping.</summary>
	High = 3,

	/// <summary>Top salience — architectural calls, hard-won lessons.</summary>
	Critical = 4,
}
