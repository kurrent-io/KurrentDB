// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.LogAbstraction;

public interface INameExistenceFilterInitializer {
	ValueTask Initialize(INameExistenceFilter filter, long truncateToPosition, CancellationToken token);
}
