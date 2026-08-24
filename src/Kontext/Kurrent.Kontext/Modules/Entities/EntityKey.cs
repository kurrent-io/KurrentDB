// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Entities;

/// <summary>
/// The type-strict match key for exact resolution, mirroring EntityId's (type, normalized name)
/// identity scheme: "apple" the organization never matches "apple" the object.
/// </summary>
public readonly record struct EntityKey(string EntityType, string NormalizedText) {
    public static EntityKey For(string entityType, string text) =>
        new(entityType, EntityId.Normalize(text));
}
