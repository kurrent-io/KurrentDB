using System.Collections;
using EventStore.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Tests.Configuration;

public class ConfigurationRootExtensionsTest {
	private const string GOSSIP_SEED = "GossipSeed";
	private const string CONFIG_FILE_KEY = "Config";

	[Fact]
	public void successful_comma_separated_value() {
		var config = new Dictionary<string, string?> {
			{ GOSSIP_SEED, "nodeb.eventstore.test:2113,nodec.eventstore.test:3113" }
		};
		IConfigurationRoot configuration = MemoryConfigurationBuilderExtensions
			.AddInMemoryCollection(new ConfigurationBuilder(), config)
			.Build();
		var values =
			EventStore.Common.Configuration.ConfigurationRootExtensions.GetCommaSeparatedValueAsArray(
				configuration, "GossipSeed");
		Assert.Equal(2, values.Length);
	}

	[Fact]
	public void invalid_delimiter() {
		var config = new Dictionary<string, string?> {
			{ GOSSIP_SEED, "nodeb.eventstore.test:2113;nodec.eventstore.test:3113" }
		};
		IConfigurationRoot configuration = MemoryConfigurationBuilderExtensions
			.AddInMemoryCollection(new ConfigurationBuilder(), config)
			.Build();

		Assert.Throws<ArgumentException>(() =>
			EventStore.Common.Configuration.ConfigurationRootExtensions.GetCommaSeparatedValueAsArray(
				configuration, "GossipSeed"));
	}

	[Fact]
	public void mixed_invalid_delimiter() {
		var config = new Dictionary<string, string?> {
			{ GOSSIP_SEED, "nodea.eventstore.test:2113,nodeb.eventstore.test:2113;nodec.eventstore.test:3113" }
		};

		var configuration = MemoryConfigurationBuilderExtensions
			.AddInMemoryCollection(new ConfigurationBuilder(), config)
			.Build();

		Assert.Throws<ArgumentException>(() =>
			EventStore.Common.Configuration.ConfigurationRootExtensions.GetCommaSeparatedValueAsArray(
				configuration, "GossipSeed"));
	}

	[Fact]
	public void user_specified_config_file_through_environment_variables_returns_true() {
		IDictionary environmentVariables = new Dictionary<string, string>();
		environmentVariables.Add("EVENTSTORE_CONFIG", "pathToConfigFileOnMachine");

		var configurationRoot = new ConfigurationBuilder()
			.Add(new EnvironmentVariablesSource(environmentVariables))
			.Build();

		var result = configurationRoot.IsUserSpecified(CONFIG_FILE_KEY);

		Assert.True(result);
	}

	[Fact]
	public void user_specified_config_file_through_command_line_returns_true() {
		var args = new string[] {
			"--config=pathToConfigFileOnMachine"
		};

		var configurationRoot = new ConfigurationBuilder()
			.Add(new CommandLineSource(args))
			.Build();

		var result = configurationRoot.IsUserSpecified(CONFIG_FILE_KEY);

		Assert.True(result);
	}

	[Fact]
	public void user_did_not_specified_config_file_returns_false() {
		var configurationRoot = new ConfigurationBuilder()
			.Add(new DefaultSource(new Dictionary<string, object> {
			}))
			.Build();

		var result = configurationRoot.IsUserSpecified(CONFIG_FILE_KEY);

		Assert.False(result);
	}
}
