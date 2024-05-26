// using Polly;
// using Polly.Contrib.WaitAndRetry;
// using Polly.Retry;
//
// namespace EventStore.Streaming.Readers;
//
// [PublicAPI]
// public class ReaderRetryPolicies {
// 	public static AsyncRetryPolicy ConstantBackoffRetryPolicy(int retries = 60, int delayMs = 500, Action<Exception, int>? onRetry = null) {
// 		var retryPolicy = Policy
// 			.Handle<RpcException>(ex => ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
// 			.WaitAndRetryAsync(Backoff.ConstantBackoff(TimeSpan.FromMilliseconds(delayMs), retries), (ex, ts, attempts, ctx) => onRetry?.Invoke(ex, attempts));
//
// 		return retryPolicy;
// 	}
// 	
// 	public static AsyncRetryPolicy ExponentialBackoffRetryPolicy(int retries = 10, int delayMs = 100) {
// 		var retryPolicy = Policy
// 			.Handle<RpcException>(ex => ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
// 			.WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMilliseconds(delayMs), retries));
//
// 		return retryPolicy;
// 	}
// }