// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Configuration;
using Kurrent.Surge.Processors.Configuration;
using KurrentDB.Core.Bus;

namespace KurrentDB.Surge.Processors;

[PublicAPI]
public record SystemProcessorOptions : ProcessorOptions {
    public SystemProcessorOptions() {
        Logging = new LoggingOptions {
            LogName = "Kurrent.Surge.SystemProcessor"
        };
    }

    public IPublisher Publisher { get; init; } = null!;
}
