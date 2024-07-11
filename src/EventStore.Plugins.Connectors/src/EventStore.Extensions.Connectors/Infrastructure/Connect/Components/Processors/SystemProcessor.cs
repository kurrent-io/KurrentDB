// ReSharper disable CheckNamespace

using EventStore.Connect.Processors.Configuration;
using EventStore.Streaming.Processors;

namespace EventStore.Connect.Processors;

public class SystemProcessor(SystemProcessorOptions options) : Processor(options) {
	public static SystemProcessorBuilder Builder => new();
}