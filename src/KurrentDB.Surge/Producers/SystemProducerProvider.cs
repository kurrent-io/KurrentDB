// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;

namespace KurrentDB.Surge.Producers;

public class SystemProducerProvider(Func<SystemProducerBuilder> builderFactory) : IProducerProvider {
    public IProducer GetProducer(Func<ProducerOptions, ProducerOptions> configure) {
        var temp = builderFactory();
        var builder = temp with {
            Options = (SystemProducerOptions) configure(temp.Options)
        };

        return builder.Create();
    }
}
