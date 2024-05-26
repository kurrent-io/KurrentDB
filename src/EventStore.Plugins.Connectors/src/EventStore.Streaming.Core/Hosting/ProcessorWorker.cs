using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.Streaming.Hosting;

public class ProcessorWorker<T> : BackgroundService where T : IProcessor {
	public ProcessorWorker(T processor, IHostApplicationLifetime hostLifetime, ILoggerFactory loggerFactory) {
		Processor = processor;
		Logger    = loggerFactory.CreateLogger(processor.ProcessorName);

		Gatekeeper = new(false);

		hostLifetime.ApplicationStarted.Register(() => Gatekeeper.Set());
	}

	public ProcessorWorker(T processor, IServiceProvider serviceProvider) : this(
		processor,
		serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
		serviceProvider.GetRequiredService<ILoggerFactory>()
	) { }

	IProcessor           Processor  { get; }
	ILogger              Logger     { get; }
	ManualResetEventSlim Gatekeeper { get; }

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		Gatekeeper.Wait(stoppingToken);
		Gatekeeper.Dispose();
		
		Logger.LogDebug("{ProcessorName} Application host ready, executing...", Processor.ProcessorName);

		await Processor.RunUntilDeactivated(stoppingToken);
	}
}