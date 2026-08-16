// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Diagnostics;

/// <summary>
/// A point of progress in a monotonic source: the position of a record and the timestamp it carries.
/// </summary>
public readonly record struct ProgressMark(long Position, DateTime Timestamp) {
    public static readonly ProgressMark Unset = new(-1, DateTime.MinValue);

    public bool IsUnset => Position < 0 || Timestamp == DateTime.MinValue;
}
