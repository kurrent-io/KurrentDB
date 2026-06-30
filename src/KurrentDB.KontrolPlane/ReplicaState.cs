// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Represents replication state of the node.
/// </summary>
/// <param name="UncommittedOffset">The latest known offset of the uncommitted log record.</param>
/// <param name="Epoch">The epoch of the database node.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ReplicaState(long UncommittedOffset, ulong Epoch) : IEntity;
