using EventStore.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Tests.Configuration;

public class CommandLineSourceTest {
	private static IConfigurationRoot BuildConfiguration(string args) {
		var sut = new CommandLineSource(args.Split());
		var configurationRoot = new ConfigurationBuilder().Add(sut).Build();
		return configurationRoot;
	}

	[Fact]
	public void normalize_keys() {
		var c = BuildConfiguration("--cluster-size 3 --log /tmp/eventstore/logs");
		Assert.Equal(3, c.GetValue<int>("ClusterSize"));
		Assert.Equal("/tmp/eventstore/logs", c.GetValue<string>("Log"));
	}

	[Fact]
	public void normalize_keys_boolean_plus_sign() {
		var c = BuildConfiguration("--whatever+");
		Assert.Equal("true", c.GetValue<string>("Whatever"));
	}

	[Fact]
	public void normalize_keys_boolean_negative_sign() {
		var c = BuildConfiguration("--whatever-");
		Assert.Equal("false", c.GetValue<string>("Whatever"));
	}

	[Fact]
	public void normalize_keys_boolean_no_value() {
		var c = BuildConfiguration("--whatever");
		Assert.Equal("true", c.GetValue<string>("Whatever"));
	}

	[Fact]
	public void normalize_keys_equals() {
		var c = BuildConfiguration("--cluster-size=3");
		Assert.Equal(3, c.GetValue<int>("ClusterSize"));
	}

	[Fact]
	public void normalize_keys_one_dash() {
		var c = BuildConfiguration("-cluster-size=3");
		Assert.Equal(3, c.GetValue<int>("ClusterSize"));
	}
}
