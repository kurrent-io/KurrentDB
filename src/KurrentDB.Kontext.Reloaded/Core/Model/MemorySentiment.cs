// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// Emotional tone of a memory's content, tagged by the enrichment pipeline — NOT agent-set.
/// </summary>
public enum MemorySentiment {
	/// <summary>Not yet classified.</summary>
	Unspecified = 0,

	/// <summary>Approving / good-news tone (a fix worked, a success).</summary>
	Positive = 1,

	/// <summary>Matter-of-fact, no clear valence.</summary>
	Neutral = 2,

	/// <summary>Adverse tone (a failure, a complaint, a broken thing).</summary>
	Negative = 3,
}
