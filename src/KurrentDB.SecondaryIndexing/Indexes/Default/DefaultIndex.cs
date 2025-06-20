// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using DotNext;
using KurrentDB.Core.Services;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndex : Disposable, ISecondaryIndex {
	public const string IndexName = $"{SystemStreams.IndexStreamPrefix}all"; }

internal record struct AllRecord(long Seq, long LogPosition);
