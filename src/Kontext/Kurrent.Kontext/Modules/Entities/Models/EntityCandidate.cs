// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Entities;

/// <summary>A catalog entity a cheaper tier surfaced but would not merge on its own.</summary>
public sealed record EntityCandidate(string EntityId, string Alias, double Similarity);
