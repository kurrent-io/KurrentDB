// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Resilience;

namespace KurrentDB.Connect.Producers.Configuration;

public record SystemProducerOptions : ProducerOptions {
    public SystemProducerOptions() {
        Logging = new LoggingOptions {
            LogName = "Kurrent.Surge.SystemProducer"
        };

        ResiliencePipelineBuilder = DefaultRetryPolicies.ExponentialBackoffPipelineBuilder();
    }

    public IPublisher Publisher { get; init; }
}
