// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Toolkit;

namespace KurrentDB.Connect.Producers.Configuration;

[PublicAPI]
public record SystemProducerBuilder : ProducerBuilder<SystemProducerBuilder, SystemProducerOptions> {
	public SystemProducerBuilder Publisher(IPublisher publisher) {
		Ensure.NotNull(publisher);
		return new() {
			Options = Options with {
				Publisher = publisher
			}
		};
	}

	public override SystemProducer Create() {
		Ensure.NotNullOrWhiteSpace(Options.ProducerId);
		Ensure.NotNull(Options.Publisher);

        return new(Options with {});

		// var options = Options with {
		// 	ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(
		// 		Options.Logging.Enabled
		// 			? Options.Logging.LoggerFactory
		// 			: NullLoggerFactory.Instance,
		// 		"ProducerResiliencePipelineTelemetryLogger"
		// 	)
		// };
		//
		// return new(Options);
	}
}
