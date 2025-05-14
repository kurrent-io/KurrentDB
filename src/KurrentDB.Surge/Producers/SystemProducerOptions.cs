// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Resilience;
using KurrentDB.Core.Bus;

namespace KurrentDB.Surge.Producers;

public record SystemProducerOptions : ProducerOptions {
    public SystemProducerOptions() {
        Logging = new() {
            LogName = "Kurrent.Surge.SystemProducer"
        };

        ResiliencePipelineBuilder = DefaultRetryPolicies.ExponentialBackoffPipelineBuilder();
    }

    public IPublisher Publisher { get; init; } = null!;
}
