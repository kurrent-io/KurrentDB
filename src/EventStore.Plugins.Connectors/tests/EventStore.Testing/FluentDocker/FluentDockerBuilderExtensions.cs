using System.Reflection;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace EventStore.Testing.FluentDocker;

[PublicAPI]
public static class FluentDockerBuilderExtensions {
	public static CompositeBuilder OverrideConfiguration(this CompositeBuilder compositeBuilder, Action<DockerComposeConfig> configure) {
		configure(GetInternalConfig(compositeBuilder));
		return compositeBuilder;

		static DockerComposeConfig GetInternalConfig(CompositeBuilder compositeBuilder) =>
			(DockerComposeConfig)typeof(CompositeBuilder)
				.GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)!
				.GetValue(compositeBuilder)!;
	}

	public static DockerComposeConfig Configuration(this ICompositeService service) =>
		(DockerComposeConfig)typeof(DockerComposeCompositeService)
			.GetProperty("Config", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(service)!;
}