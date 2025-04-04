// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Chunks;

public abstract record TFChunkInfo(string FileName);
public record LatestVersion(string FileName, int Start, int End) : TFChunkInfo(FileName);
public record OldVersion(string FileName, int Start) : TFChunkInfo(FileName);
public record MissingVersion(string FileName, int ChunkNumber) : TFChunkInfo(FileName);
