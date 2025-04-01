// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.POC.IO.Core;

public interface ISink {

	public Uri Config { get; }

	public Task Write(Event e, CancellationToken ct);
}
