// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Data;

/// <summary>One search result: the stored memory and its relevance score (larger = better).</summary>
public readonly record struct MemorySearchHit(Contracts.StoredMemory Memory, double Score);
