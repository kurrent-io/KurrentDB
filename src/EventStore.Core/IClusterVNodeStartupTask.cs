// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core;

// Implementations of this interface are run to completion during startup
public interface IClusterVNodeStartupTask {
	ValueTask Run(CancellationToken token);
}
