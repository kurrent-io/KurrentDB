// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Processors.Configuration;

namespace KurrentDB.Connect.Processors.Configuration;

[PublicAPI]
public record SystemProcessorOptions : ProcessorOptions {
    public SystemProcessorOptions() {
        Logging = new LoggingOptions {
            LogName = "Kurrent.Surge.SystemProcessor"
        };
    }

    public IPublisher Publisher { get; init; }
}
