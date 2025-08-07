// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Storage;

public record struct ReferenceRecord(int Id, string Name);

public record struct IndexQueryRecord(uint RowId, long LogPosition);
