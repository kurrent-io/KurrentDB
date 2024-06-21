// ReSharper disable CheckNamespace

using EventStore.Streaming.Processors.Configuration;

namespace EventStore.Streaming.Processors;

public class SystemProcessor(SystemProcessorOptions options) : Processor(options) {
	public static SystemProcessorBuilder Builder => new();
}