// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Threading.Tasks;

namespace KurrentDB.Core.Hosting;

public interface IStartupWorkCompletionMonitor {
	Task WhenCompletedAsync();
}
