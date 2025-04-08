// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.TransactionLog.Scavenging.Data;

// These are stored in the data of the payload record
public class ScavengePointPayload {
	public int Threshold { get; set; }

	public byte[] ToJsonBytes() =>
		Json.ToJsonBytes(this);

	public static ScavengePointPayload FromBytes(ReadOnlyMemory<byte> bytes) =>
		Json.ParseJson<ScavengePointPayload>(bytes);
}
