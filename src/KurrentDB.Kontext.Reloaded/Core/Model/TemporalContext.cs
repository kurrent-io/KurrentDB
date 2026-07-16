// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The world-time span a memory's content refers to, as perceived from its text (may be fuzzy). This is
/// <em>content</em> time — distinct from the storage clocks (<see cref="StoredMemory.RetainedAt"/> and
/// <see cref="StoredMemory.LastAccessedAt"/>). A point-in-time event sets <see cref="To"/> equal
/// to <see cref="From"/>; a null end means ongoing / open-ended.
/// </summary>
/// <param name="From">Start of the state, or the moment of a point event.</param>
/// <param name="To">End of the interval; null ⇒ ongoing.</param>
public sealed record TemporalContext(DateTimeOffset From, DateTimeOffset? To = null);
