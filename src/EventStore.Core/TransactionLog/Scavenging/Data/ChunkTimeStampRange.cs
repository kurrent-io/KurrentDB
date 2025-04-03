// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Scavenging;

// store a range per chunk so that the calculator can definitely get a timestamp range for each event
// that is guaranteed to contain the real timestamp of that event.
public readonly record struct ChunkTimeStampRange(DateTime Min, DateTime Max);
