using Polly;
using Polly.Retry;

namespace EventStore.Streaming.Resilience;

[PublicAPI]
public static class RetryPolicies {
	static readonly PredicateBuilder<object> RetryOnTransientErrors = new PredicateBuilder()
		.Handle<StreamingError>(ex => ex.IsTransient);
	
	public static RetryStrategyOptions ConstantBackoffRetryOptions(int retries = 60, int delayMs = 500) {
		return new RetryStrategyOptions {
			Name             = "ConstantBackoffStrategy",
			MaxRetryAttempts = retries,
			Delay            = TimeSpan.FromMilliseconds(delayMs),
			BackoffType      = DelayBackoffType.Constant,
			UseJitter        = true,
			ShouldHandle     = RetryOnTransientErrors,
		};
	}
	
	public static RetryStrategyOptions ExponentialBackoffRetryOptions(int retries = 10, int delayMs = 100) {
		return new RetryStrategyOptions {
			Name             = "ExponentialBackoffStrategy",
			MaxRetryAttempts = retries,
			Delay            = TimeSpan.FromMilliseconds(delayMs),
			BackoffType      = DelayBackoffType.Exponential,
			UseJitter        = true,
			ShouldHandle     = RetryOnTransientErrors
		};
	}
	
	public static ResiliencePipelineBuilder ConstantBackoffPipeline(int retries = 60, int delayMs = 500) => 
		new ResiliencePipelineBuilder()
			.With(x => x.Name = nameof(ConstantBackoffPipeline))
			.AddRetry(ConstantBackoffRetryOptions(retries, delayMs));
	
	public static ResiliencePipelineBuilder ExponentialBackoffPipeline(int retries = 10, int delayMs = 100) => 
		new ResiliencePipelineBuilder()
			.With(x => x.Name = nameof(ExponentialBackoffPipeline))
			.AddRetry(ExponentialBackoffRetryOptions(retries, delayMs));
}