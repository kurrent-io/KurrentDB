namespace EventStore.Streaming.Processors;

public delegate Task OnProcessorDeactivated(IProcessorMetadata processor, Exception? exception);

[PublicAPI]
public static class ProcessorExtensions {
	public static async Task RunUntilDeactivated(this IProcessor processor, CancellationToken stoppingToken) {
		await processor.Activate(stoppingToken);
		
		try {
			await processor.Stopped;
		}
		catch (OperationCanceledException) {
			// this is expected
		}
	}

	public static async Task RunUntilDeactivated(this IProcessor processor, OnProcessorDeactivated onDeactivated, CancellationToken stoppingToken) {
		await processor.Activate(stoppingToken);

		try {
			await processor.Stopped;
			await onDeactivated(processor, null);
		}
		catch (OperationCanceledException) {
			await onDeactivated(processor, null);
		}
		catch (Exception ex) {
			await onDeactivated(processor, ex);
		}
	}
}