namespace EventStore.Streaming;

static class Tasks {
    public static async Task SafeDelay(TimeSpan delay, CancellationToken cancellationToken = default) {
        try {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            // fail gracefully
        }
    }
    
    public static async Task SafeDelay(int delayMs, CancellationToken cancellationToken = default) {
	    try {
		    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
	    }
	    catch (OperationCanceledException) {
		    // fail gracefully
	    }
    }
}