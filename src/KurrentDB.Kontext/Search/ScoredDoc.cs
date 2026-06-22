// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

/// <summary>
/// A document identifier (typically a KurrentDB prepare position) paired with a
/// relevance score. Used as the common shape across the search pipeline.
/// </summary>
public readonly record struct ScoredDoc(ulong DocId, float Score);