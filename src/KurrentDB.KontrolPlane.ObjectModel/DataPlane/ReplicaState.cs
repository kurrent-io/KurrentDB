// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.DataPlane;

/// <summary>
/// Represents replication state of the node.
/// </summary>
/// <param name="Epoch">The epoch of the database node.</param>
/// <param name="WriterCheckpoint">The latest known offset of the uncommitted log record.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ReplicaState(ulong Epoch, long WriterCheckpoint, long ChaserCheckpoint, int Priority) : IModelEntity;
