// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// Produces events in log-position order from a specific source.
/// All implementations checkpoint by log position (TFPos).
/// </summary>
public interface IReadStrategy : IAsyncDisposable {
	IAsyncEnumerable<ReadResponse> ReadFrom(TFPos checkpoint, CancellationToken ct);
}
