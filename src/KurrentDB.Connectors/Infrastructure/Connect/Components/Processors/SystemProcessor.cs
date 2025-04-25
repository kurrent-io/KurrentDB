// ReSharper disable CheckNamespace

using KurrentDB.Connect.Processors.Configuration;
using Kurrent.Surge.Processors;

namespace KurrentDB.Connect.Processors;

public class SystemProcessor(SystemProcessorOptions options) : Processor(options) {
	public static SystemProcessorBuilder Builder => new();
}
