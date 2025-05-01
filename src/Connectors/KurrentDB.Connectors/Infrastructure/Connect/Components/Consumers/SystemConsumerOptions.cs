// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Consumers.Configuration;

namespace KurrentDB.Connect.Consumers.Configuration;

public record SystemConsumerOptions : ConsumerOptions {
    public SystemConsumerOptions() {
        Logging = new LoggingOptions {
            LogName = "Kurrent.Surge.SystemConsumer"
        };

        Filter = ConsumeFilter.ExcludeSystemEvents();
    }

    public IPublisher Publisher { get; init; }
}
