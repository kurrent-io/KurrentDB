namespace EventStore.Streaming;

static class CancellationTokenSourceExtensions {
	public static async Task WaitUntilCancelled(this CancellationTokenSource cancellator, int delayMs = 50) {
		while (!cancellator.IsCancellationRequested)
			await Task.Delay(delayMs);
	}
}