// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Resilience;
using KurrentDB.Core;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Surge.Producers;

public record SystemProducerOptions : ProducerOptions {
    public SystemProducerOptions() {
        Logging = new() {
            LogName = "Kurrent.Surge.SystemProducer"
        };

        ResiliencePipelineBuilder = DefaultRetryPolicies.ExponentialBackoffPipelineBuilder();
    }

    public ISystemClient Client { get; init; } = null!;
    public StreamsServiceClient? StreamsClient { get; init; } = null!;
}
