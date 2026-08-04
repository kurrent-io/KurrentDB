// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Rewrites a raw query into a richer one before retrieval — synonym expansion, spelling
/// normalization, or a HyDE-style hypothetical answer.
/// </summary>
public interface IQueryExpander {
    ValueTask<string> ExpandAsync(string query, CancellationToken ct = default);
}
