// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using Kurrent.Surge.Readers.Configuration;
using KurrentDB.Core;

namespace KurrentDB.Surge.Readers;

[PublicAPI]
public record SystemReaderBuilder : ReaderBuilder<SystemReaderBuilder, SystemReaderOptions> {
    public SystemReaderBuilder Client(ISystemClient client) {
        Ensure.NotNull(client);
        return new() {
            Options = Options with {
                Client = client
            }
        };
    }

	public override SystemReader Create() {
        Ensure.NotNullOrWhiteSpace(Options.ReaderId);
        Ensure.NotNull(Options.Client);

        return new(Options with {});

        // var options = Options with {
        //     ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(Options.Logging.Enabled
        //             ? Options.Logging.LoggerFactory
        //             : NullLoggerFactory.Instance,
        //         "ReaderResiliencePipelineTelemetryLogger")
        // };

		// return new(options);
	}
}
