using EventStore.Core;
using EventStore.Plugins;

namespace EventStore.Testing.Fixtures;

static class ClusterVNodeOptionsExtensions {
	public static ClusterVNodeOptions WithPlugableComponent(this ClusterVNodeOptions options, IPlugableComponent plugableComponent) =>
		options with { PlugableComponents = [..options.PlugableComponents, plugableComponent] };

	// public static ClusterVNodeOptions NoTelemetry(this ClusterVNodeOptions options) => options with {
	// 	Application = options.Application with {
	// 		TelemetryOptout = true
	// 	}
	// };
	//
	// public static ClusterVNodeOptions RunInMemory(this ClusterVNodeOptions options) => options with {
	// 	Database = options.Database with {
	// 		MemDb = true,
	// 		Db = new ClusterVNodeOptions().Database.Db
	// 	}
	// };
	//
	// public static ClusterVNodeOptions Insecure(this ClusterVNodeOptions options) => options with {
	// 	Application = options.Application with {
	// 		Insecure = true
	// 	},
	// 	ServerCertificate = null,
	// 	TrustedRootCertificates = null
	// };
}