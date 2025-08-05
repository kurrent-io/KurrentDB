// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SchemaRegistry.Infrastructure.Eventuous;

public static class EventuousChangesExtensions {
    public static T GetSingleEvent<T>(this IEnumerable<Change> changes) =>
        changes
            .Select(x => x.Event is T value ? value : default)
            .SingleOrDefault(x => x is not null) ?? throw new($"Failed to get event {typeof(T).Name}");
}