using Polly;
using Polly.Telemetry;

namespace EventStore.Streaming.Resilience;

class ResilienceTelemetryListener(ILogger logger) : TelemetryListener {
	public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args) {
		var logLevel = args.Event.Severity switch {
			ResilienceEventSeverity.None        => LogLevel.None,
			ResilienceEventSeverity.Debug       => LogLevel.Debug,
			ResilienceEventSeverity.Information => LogLevel.Information,
			ResilienceEventSeverity.Warning     => LogLevel.Warning,
			ResilienceEventSeverity.Error       => LogLevel.Error,
			ResilienceEventSeverity.Critical    => LogLevel.Critical,
			_                                   => throw new ArgumentOutOfRangeException()
		};

		if (args.Arguments is ExecutionAttemptArguments executionAttemptArguments) {
			if (logLevel >= LogLevel.Error) {
				logger.Log(
					logLevel,
					args.Outcome?.Exception,
					"{Operation} {EventName} {@Source} {@Arguments} {ErrorMessage}",
					args.Context.OperationKey,
					args.Event.EventName,
					args.Source,
					executionAttemptArguments, 
					args.Outcome?.Exception?.Message
				);
			}
			else {
				logger.Log(
					logLevel,
					"{Operation} {EventName} {@Source} {@Arguments} {ErrorMessage}",
					args.Context.OperationKey,
					args.Event.EventName,
					args.Source,
					executionAttemptArguments, 
					args.Outcome?.Exception?.Message
				);
			}
		}
	}
}

/// <summary>
/// Telemetry extensions for the <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
public static class TelemetryResiliencePipelineBuilderExtensions {
	/// <summary>
	/// Enables telemetry for this builder.
	/// </summary>
	public static TBuilder ConfigureTelemetry<TBuilder>(this TBuilder builder, ILoggerFactory loggerFactory, string? loggerName = null)
		where TBuilder : ResiliencePipelineBuilderBase {
		Ensure.NotNull(builder);
		Ensure.NotNull(loggerFactory);

		var options = new TelemetryOptions {
			// LoggerFactory = loggerFactory, 
			TelemetryListeners = {
				new ResilienceTelemetryListener(loggerFactory.CreateLogger(loggerName ?? "ResilienceTelemetryLogger"))
			}
		};

		return builder.ConfigureTelemetry(options);
	}
}