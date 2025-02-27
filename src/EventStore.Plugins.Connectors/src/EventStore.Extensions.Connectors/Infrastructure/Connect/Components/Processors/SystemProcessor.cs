// ReSharper disable CheckNamespace

using EventStore.Connect.Processors.Configuration;
using Kurrent.Surge.Processors;

namespace EventStore.Connect.Processors;

public class SystemProcessor(SystemProcessorOptions options) : Processor(options) {
	public static SystemProcessorBuilder Builder => new();
}