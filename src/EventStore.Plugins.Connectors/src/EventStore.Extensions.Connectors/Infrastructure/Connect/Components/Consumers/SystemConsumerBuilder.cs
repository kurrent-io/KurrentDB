// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Toolkit;
using Microsoft.Extensions.Logging;
using Polly.Telemetry;

namespace EventStore.Connect.Consumers.Configuration;

[PublicAPI]
public record SystemConsumerBuilder : ConsumerBuilder<SystemConsumerBuilder, SystemConsumerOptions> {
    public SystemConsumerBuilder Publisher(IPublisher publisher) {
        Ensure.NotNull(publisher);
        return new() {
            Options = Options with {
                Publisher = publisher
            }
        };
    }

    public override SystemConsumer Create() {
        Ensure.NotNullOrWhiteSpace(Options.ConsumerId);
        Ensure.NotNullOrWhiteSpace(Options.SubscriptionName);
        Ensure.NotNull(Options.Publisher);

        return new(Options with {});

        //  var telemetryOptions = new TelemetryOptions {
        //     // LoggerFactory = Options.Logging.LoggerFactory, // this just outputs all things polly but its just noise
        //     TelemetryListeners = {
        //         new SystemConsumerResilienceTelemetryListener(
        //             Options.Logging.LoggerFactory.CreateLogger($"ConsumerResiliencePipelineTelemetryLogger({Options.ConsumerId})")
        //         )
        //     }
        // };
        //
        // var options = Options with {
        //     ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(telemetryOptions)
        // };

        // return new(options);
    }
}

class SystemConsumerResilienceTelemetryListener(ILogger logger) : TelemetryListener {
	public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args) {
		var logLevel = args.Event.Severity switch {
			ResilienceEventSeverity.None        => LogLevel.None,
			ResilienceEventSeverity.Debug       => LogLevel.Debug,
			ResilienceEventSeverity.Information => LogLevel.Information,
			ResilienceEventSeverity.Warning     => LogLevel.Warning,
			ResilienceEventSeverity.Error       => LogLevel.Error,
			ResilienceEventSeverity.Critical    => LogLevel.Critical
		};

		if (args.Arguments is ExecutionAttemptArguments executionAttemptArguments) {
            if (args.Outcome?.Exception is OperationCanceledException)
                logger.Log(
                    LogLevel.Debug,
                    "{Operation} {EventName} {ErrorMessage}",
                    args.Context.OperationKey,
                    args.Event.EventName,
                    args.Outcome?.Exception?.Message
                );
            else
                logger.Log(
                    logLevel,
                    args.Outcome?.Exception,
                    "{Operation} {EventName} {@Arguments} {ErrorMessage}",
                    args.Context.OperationKey,
                    args.Event.EventName,
                    executionAttemptArguments,
                    args.Outcome?.Exception?.Message
                );
		}
	}
}