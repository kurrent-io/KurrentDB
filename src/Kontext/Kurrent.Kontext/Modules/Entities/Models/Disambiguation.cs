// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Entities;

/// <summary>One name the cheaper tiers left unresolved, with the entities they thought it might be.</summary>
public sealed record Disambiguation(EntityKey Key, string Text, IReadOnlyList<EntityCandidate> Candidates);
